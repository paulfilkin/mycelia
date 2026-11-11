using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mycelia.Ai.Mcp;

/// <summary>
/// Browser sign-in for MCP servers at a web address, by the MCP authorization rules (2025-06-18), ported from
/// multifariousStudio (TDD 22.5):
/// <list type="number">
/// <item>The server answers 401 with WWW-Authenticate naming its protected resource metadata (RFC 9728); otherwise the
/// metadata is looked for at /.well-known/oauth-protected-resource with and without the server's path.</item>
/// <item>That metadata names the authorization server, whose metadata (RFC 8414, or OpenID Connect discovery) gives the
/// sign-in, token and registration addresses.  Without either, the 2025-03-26 defaults apply: /authorize, /token and
/// /register at the server's origin.</item>
/// <item>The client is the person's own app (client ID and secret), or one registered automatically (RFC 7591) where
/// the service allows it.</item>
/// <item>The browser opens the authorization code request with PKCE (S256), a random state and the server as resource
/// (RFC 8707); the answer comes back to a loopback address this class listens on, and the code is exchanged for tokens.</item>
/// <item>Tokens are kept encrypted and refreshed before they expire, or when the server refuses one.</item>
/// </list>
/// </summary>
public static class McpOAuth
{
    /// <summary>How long the browser has to come back (the person signs in and approves).</summary>
    public static TimeSpan SignInTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A token this close to expiry is refreshed before use.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    /// <summary>Raised after signing in or refreshing changed a server's tokens (on any thread), so they can be saved.</summary>
    public static event Action<McpServerConfig>? TokensChanged;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    // ---- Discovery --------------------------------------------------------------------------------------

    public sealed record Discovery(string Resource, string Issuer, string AuthorizationEndpoint, string TokenEndpoint,
        string? RegistrationEndpoint, IReadOnlyList<string> TokenAuthMethods, IReadOnlyList<string> CodeChallengeMethods,
        string? Scope, string Source);

    /// <summary>The WWW-Authenticate parameters of a 401 (resource_metadata, scope, error...).</summary>
    public static Dictionary<string, string> ChallengeParameters(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter == null)
                continue;
            foreach (Match match in Regex.Matches(header.Parameter, "([A-Za-z_]+)\\s*=\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^,\\s]+))"))
                result[match.Groups[1].Value] = match.Groups[2].Success ? match.Groups[2].Value.Replace("\\\"", "\"") : match.Groups[3].Value;
        }
        return result;
    }

    /// <summary>The server's address as the canonical resource: scheme and host in lower case, no fragment, no trailing slash.</summary>
    public static string CanonicalResource(Uri server)
    {
        var builder = new UriBuilder(server) { Fragment = string.Empty, Scheme = server.Scheme.ToLowerInvariant(), Host = server.Host.ToLowerInvariant() };
        string text = builder.Uri.GetLeftPart(UriPartial.Query);
        return server.Query.Length == 0 ? text.TrimEnd('/') : text;
    }

    public static async Task<Discovery> DiscoverAsync(HttpClient http, McpServerConfig config, Action<string> log, CancellationToken cancel)
    {
        var server = new Uri(config.Url.Trim());
        string origin = server.GetLeftPart(UriPartial.Authority);
        string path = server.AbsolutePath.TrimEnd('/');

        // 1. Ask the server without a token: a 401 names its resource metadata (and perhaps the scopes it needs).
        string? metadataUrl = null;
        string? scope = null;
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Post, server);
            foreach (var (key, value) in config.Headers)
                if (!key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    probe.Headers.TryAddWithoutValidation(key, SecretStore.Unprotect(value));
            probe.Content = new StringContent(InitializeProbe, new UTF8Encoding(false), "application/json");
            probe.Headers.Accept.ParseAdd("application/json");
            probe.Headers.Accept.ParseAdd("text/event-stream");
            using var answer = await http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (answer.StatusCode == HttpStatusCode.Unauthorized)
            {
                var challenge = ChallengeParameters(answer);
                challenge.TryGetValue("resource_metadata", out metadataUrl);
                challenge.TryGetValue("scope", out scope);
                log($"Sign-in: the server asks for a token{(metadataUrl != null ? ", resource metadata " + metadataUrl : string.Empty)}.");
            }
            else
            {
                log($"Sign-in: the server answered {(int)answer.StatusCode} without a token.");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"{server.Host} couldn't be reached to start signing in: {ex.Message}");
        }

        // 2. The protected resource metadata (RFC 9728).
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(metadataUrl))
            candidates.Add(metadataUrl);
        if (path.Length > 0)
            candidates.Add(origin + "/.well-known/oauth-protected-resource" + path);
        candidates.Add(origin + "/.well-known/oauth-protected-resource");
        JsonObject? resourceMetadata = null;
        foreach (var candidate in candidates.Distinct())
        {
            resourceMetadata = await GetJsonAsync(http, candidate, cancel);
            if (resourceMetadata != null)
            {
                log("Sign-in: resource metadata from " + candidate);
                break;
            }
        }

        string resource = resourceMetadata?["resource"]?.GetValue<string>() ?? CanonicalResource(server);
        string issuer = (resourceMetadata?["authorization_servers"] as JsonArray)?.FirstOrDefault()?.GetValue<string>() ?? origin;
        if (scope == null && resourceMetadata?["scopes_supported"] is JsonArray supported && supported.Count > 0)
            scope = string.Join(' ', supported.Select(s => s?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)));

        // 3. The authorization server's metadata (RFC 8414, then OpenID Connect discovery), path-aware.
        var issuerUri = new Uri(issuer);
        string issuerOrigin = issuerUri.GetLeftPart(UriPartial.Authority);
        string issuerPath = issuerUri.AbsolutePath.TrimEnd('/');
        var serverCandidates = issuerPath.Length > 0
            ? new[] { $"{issuerOrigin}/.well-known/oauth-authorization-server{issuerPath}", $"{issuerOrigin}/.well-known/openid-configuration{issuerPath}", $"{issuerOrigin}{issuerPath}/.well-known/openid-configuration" }
            : new[] { $"{issuerOrigin}/.well-known/oauth-authorization-server", $"{issuerOrigin}/.well-known/openid-configuration" };
        JsonObject? serverMetadata = null;
        string source = "defaults";
        foreach (var candidate in serverCandidates)
        {
            serverMetadata = await GetJsonAsync(http, candidate, cancel);
            if (serverMetadata?["authorization_endpoint"] != null && serverMetadata["token_endpoint"] != null)
            {
                source = candidate;
                break;
            }
            serverMetadata = null;
        }

        static List<string> Strings(JsonNode? node) =>
            (node as JsonArray)?.Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList() ?? new List<string>();

        if (serverMetadata == null)
        {
            // 2025-03-26 fallback: the default endpoints at the authorization server's origin.
            log("Sign-in: no authorization server metadata; using " + issuerOrigin + "/authorize, /token and /register.");
            return new Discovery(resource, issuer, issuerOrigin + "/authorize", issuerOrigin + "/token", issuerOrigin + "/register",
                new List<string>(), new List<string> { "S256" }, scope, source);
        }
        log("Sign-in: authorization server metadata from " + source);
        return new Discovery(resource, serverMetadata["issuer"]?.GetValue<string>() ?? issuer,
            serverMetadata["authorization_endpoint"]!.GetValue<string>(), serverMetadata["token_endpoint"]!.GetValue<string>(),
            serverMetadata["registration_endpoint"]?.GetValue<string>(),
            Strings(serverMetadata["token_endpoint_auth_methods_supported"]), Strings(serverMetadata["code_challenge_methods_supported"]),
            scope, source);
    }

    private const string InitializeProbe =
        "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"" + McpClient.ProtocolVersion +
        "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"" + McpClient.ClientName + "\",\"version\":\"1\"}}}";

    private static async Task<JsonObject?> GetJsonAsync(HttpClient http, string url, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
                return null;
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel)) as JsonObject;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or UriFormatException)
        {
            return null;
        }
    }

    // ---- Signing in ---------------------------------------------------------------------------------------

    /// <summary>
    /// Signs in through the browser and keeps the tokens in <paramref name="config"/>.  <paramref name="openBrowser"/>
    /// opens the sign-in address (the system browser in the app; checks follow it themselves).
    /// </summary>
    public static async Task SignInAsync(McpServerConfig config, Action<Uri> openBrowser, Action<string> log, CancellationToken cancel)
    {
        var oauth = config.OAuth;
        if (!Uri.TryCreate(config.Url.Trim(), UriKind.Absolute, out _))
            throw new McpException("Type the server's address first.");
        if (!Uri.TryCreate(oauth.CallbackAddress.Trim(), UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttp
            || !(callback.IsLoopback || callback.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) || callback.Port <= 0)
            throw new McpException("The callback address must be on this computer with a port, for example " + McpOAuthSettings.DefaultCallback + ".");

        using var http = NewHttp();
        var found = await DiscoverAsync(http, config, log, cancel);
        if (found.CodeChallengeMethods.Count > 0 && !found.CodeChallengeMethods.Contains("S256"))
            throw new McpException($"{found.Issuer} doesn't offer PKCE with S256, which the MCP sign-in requires.");

        // The client: the person's own app, else one registered automatically.
        string clientId = oauth.ClientId.Trim();
        string clientSecret = SecretStore.Unprotect(oauth.ClientSecret);
        string redirect = callback.GetLeftPart(UriPartial.Path);
        if (clientId.Length == 0)
        {
            if (oauth.RegisteredClientId.Length > 0 && oauth.RegisteredForCallback == redirect && oauth.Issuer == found.Issuer)
            {
                clientId = oauth.RegisteredClientId;
                clientSecret = SecretStore.Unprotect(oauth.RegisteredClientSecret);
            }
            else
            {
                (clientId, clientSecret) = await RegisterAsync(http, found, redirect, log, cancel);
                oauth.RegisteredClientId = clientId;
                oauth.RegisteredClientSecret = SecretStore.Protect(clientSecret);
                oauth.RegisteredForCallback = redirect;
            }
        }

        // PKCE (RFC 7636): 32 random bytes as the verifier, its SHA-256 as the challenge; a random state.
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));
        string scopes = oauth.Scopes.Trim().Length > 0 ? oauth.Scopes.Trim() : found.Scope ?? string.Empty;

        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", clientId),
            new("redirect_uri", redirect),
            new("code_challenge", challenge),
            new("code_challenge_method", "S256"),
            new("state", state)
        };
        if (scopes.Length > 0)
            query.Add(new("scope", scopes));
        if (oauth.SendResource)
            query.Add(new("resource", found.Resource));
        foreach (var (key, value) in oauth.ExtraParameters)
            if (key.Trim().Length > 0 && query.All(p => p.Key != key.Trim()))
                query.Add(new(key.Trim(), value));
        var signIn = new Uri(found.AuthorizationEndpoint + (found.AuthorizationEndpoint.Contains('?') ? "&" : "?") + Form(query));

        // Listen on the callback address before the browser opens.
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        waiting.CancelAfter(SignInTimeout);
        var answerTask = WaitForCallbackAsync(callback, waiting.Token);
        log("Sign-in: opening the browser at " + found.AuthorizationEndpoint);
        openBrowser(signIn);
        Dictionary<string, string> answer;
        try
        {
            answer = await answerTask;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new McpException($"The browser didn't come back within {SignInTimeout.TotalMinutes:F0} minutes. Sign in again.");
        }

        if (answer.TryGetValue("error", out var error))
            throw new McpException($"Sign-in refused: {error}" + (answer.TryGetValue("error_description", out var description) ? $" ({description})" : string.Empty)
                + (error is "invalid_request" or "invalid_client" or "unauthorized_client" ? ". Check the client ID and that the callback address is registered with the app exactly as shown." : "."));
        if (!answer.TryGetValue("state", out var returned) || returned != state)
            throw new McpException("The sign-in answer didn't match the request (state), so it was refused. Sign in again.");
        if (!answer.TryGetValue("code", out var code) || code.Length == 0)
            throw new McpException("The sign-in answer had no authorization code.");

        // Exchange the code for tokens.
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirect),
            new("code_verifier", verifier)
        };
        if (oauth.SendResource)
            form.Add(new("resource", found.Resource));
        string method = AuthMethod(found.TokenAuthMethods, clientSecret);
        var tokens = await TokenRequestAsync(http, found.TokenEndpoint, form, clientId, clientSecret, method, cancel);

        oauth.Issuer = found.Issuer;
        oauth.AuthorizationEndpoint = found.AuthorizationEndpoint;
        oauth.TokenEndpoint = found.TokenEndpoint;
        oauth.TokenAuthMethod = method;
        oauth.Resource = found.Resource;
        Keep(oauth, tokens, keepRefreshToken: false);
        log($"Sign-in: signed in{(oauth.ExpiresUtc is { } until ? $", token valid until {until.ToLocalTime():HH:mm}" : string.Empty)}{(oauth.RefreshToken.Length > 0 ? ", with a refresh token" : ", no refresh token")}.");
        TokensChanged?.Invoke(config);
    }

    /// <summary>Dynamic client registration (RFC 7591) as a public client using PKCE.</summary>
    private static async Task<(string Id, string Secret)> RegisterAsync(HttpClient http, Discovery found, string redirect, Action<string> log, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(found.RegistrationEndpoint))
            throw new McpException($"{found.Issuer} doesn't register apps automatically. Create an app in the service's developer console, register {redirect} as its redirect address, and enter its client ID (and secret).");
        var body = new JsonObject
        {
            ["client_name"] = McpClient.ClientName,
            ["redirect_uris"] = new JsonArray(redirect),
            ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
            ["response_types"] = new JsonArray("code"),
            ["token_endpoint_auth_method"] = "none"
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, found.RegistrationEndpoint) { Content = new StringContent(body.ToJsonString(), new UTF8Encoding(false), "application/json") };
        using var response = await http.SendAsync(request, cancel);
        string text = await response.Content.ReadAsStringAsync(cancel);
        var json = TryParse(text);
        if (!response.IsSuccessStatusCode || json?["client_id"]?.GetValue<string>() is not { Length: > 0 } id)
        {
            string why = json?["error_description"]?.GetValue<string>() ?? json?["error"]?.GetValue<string>() ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            throw new McpException($"{found.Issuer} wouldn't register Mycelia automatically ({why}). Many services only allow that for apps they know: create your own app in the service's developer console, register {redirect} as its redirect address, and enter its client ID (and secret).");
        }
        log("Sign-in: registered as client " + id);
        return (id, json["client_secret"]?.GetValue<string>() ?? string.Empty);
    }

    /// <summary>client_secret_basic where offered (or nothing is listed), else client_secret_post; none for a public client.</summary>
    private static string AuthMethod(IReadOnlyList<string> supported, string secret)
    {
        if (secret.Length == 0)
            return "none";
        if (supported.Count == 0 || supported.Contains("client_secret_basic"))
            return "client_secret_basic";
        return supported.Contains("client_secret_post") ? "client_secret_post" : "client_secret_basic";
    }

    private static async Task<JsonObject> TokenRequestAsync(HttpClient http, string endpoint, List<KeyValuePair<string, string>> form,
        string clientId, string clientSecret, string method, CancellationToken cancel)
    {
        var fields = new List<KeyValuePair<string, string>>(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (method == "client_secret_basic")
        {
            // RFC 6749 2.3.1: both parts form-encoded before base64.
            string pair = Uri.EscapeDataString(clientId) + ":" + Uri.EscapeDataString(clientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
        }
        else
        {
            fields.Add(new("client_id", clientId));
            if (method == "client_secret_post")
                fields.Add(new("client_secret", clientSecret));
        }
        request.Content = new FormUrlEncodedContent(fields);
        request.Headers.Accept.ParseAdd("application/json");
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException("The sign-in service couldn't be reached: " + ex.Message);
        }
        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancel);
            var json = TryParse(text);
            if (!response.IsSuccessStatusCode || json?["access_token"] == null)
            {
                string error = json?["error"]?.GetValue<string>() ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
                string? description = json?["error_description"]?.GetValue<string>();
                throw new McpTokenException(error, $"The sign-in service refused the token request: {error}{(description != null ? $" ({description})" : string.Empty)}.");
            }
            return json;
        }
    }

    private static void Keep(McpOAuthSettings oauth, JsonObject tokens, bool keepRefreshToken)
    {
        oauth.AccessToken = SecretStore.Protect(tokens["access_token"]!.GetValue<string>());
        string? refresh = tokens["refresh_token"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(refresh))
            oauth.RefreshToken = SecretStore.Protect(refresh);
        else if (!keepRefreshToken)
            oauth.RefreshToken = string.Empty;
        oauth.ObtainedUtc = DateTime.UtcNow;
        oauth.ExpiresUtc = ReadSeconds(tokens["expires_in"]) is { } seconds ? oauth.ObtainedUtc.Value.AddSeconds(seconds)
            : JwtExpiry(tokens["access_token"]!.GetValue<string>());
        if (tokens["scope"]?.GetValue<string>() is { } granted)
            oauth.GrantedScopes = granted;
    }

    /// <summary>
    /// The expiry written in a JWT access token's "exp" claim (seconds since 1970), for answers without expires_in.
    /// Null when the token isn't a JWT or has no exp.
    /// </summary>
    public static DateTime? JwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;
        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var claims = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as JsonObject;
            return ReadSeconds(claims?["exp"]) is { } exp ? DateTime.UnixEpoch.AddSeconds(exp) : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static double? ReadSeconds(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue(out double number))
            return number;
        return value.TryGetValue(out string? text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : null;
    }

    // ---- Using and refreshing tokens --------------------------------------------------------------------------

    /// <summary>
    /// The access token to send, refreshed first when it has expired (or is about to) or when <paramref name="force"/>
    /// says the server refused it; null when not signed in or the refresh failed (the tokens are then forgotten when the
    /// service says the refresh token is no good).
    /// </summary>
    public static async Task<string?> AccessTokenAsync(McpServerConfig config, HttpClient http, bool force, Action<string> log, CancellationToken cancel)
    {
        var oauth = config.OAuth;
        if (!oauth.Enabled || !oauth.IsSignedIn)
            return null;
        var gate = Locks.GetOrAdd(config.Id.Length > 0 ? config.Id : config.Url, _ => new SemaphoreSlim(1, 1));
        string seen = oauth.AccessToken;
        await gate.WaitAsync(cancel);
        try
        {
            // Another request may have refreshed it while this one waited.
            if (force && oauth.AccessToken != seen)
                return SecretStore.Unprotect(oauth.AccessToken);
            bool expired = oauth.ExpiresUtc is { } until && until - Margin(oauth) <= DateTime.UtcNow;
            if (!force && !expired)
                return SecretStore.Unprotect(oauth.AccessToken);
            if (oauth.RefreshToken.Length == 0 || oauth.TokenEndpoint.Length == 0)
            {
                log("Sign-in: the token has expired and there is no refresh token; sign in again.");
                return null;
            }
            string clientId = oauth.ClientId.Trim().Length > 0 ? oauth.ClientId.Trim() : oauth.RegisteredClientId;
            string secret = SecretStore.Unprotect(oauth.ClientId.Trim().Length > 0 ? oauth.ClientSecret : oauth.RegisteredClientSecret);
            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "refresh_token"),
                new("refresh_token", SecretStore.Unprotect(oauth.RefreshToken))
            };
            if (oauth.SendResource && oauth.Resource.Length > 0)
                form.Add(new("resource", oauth.Resource));
            string method = oauth.TokenAuthMethod.Length > 0 && (secret.Length > 0 || oauth.TokenAuthMethod == "none") ? oauth.TokenAuthMethod : AuthMethod(Array.Empty<string>(), secret);
            try
            {
                var tokens = await TokenRequestAsync(http, oauth.TokenEndpoint, form, clientId, secret, method, cancel);
                Keep(oauth, tokens, keepRefreshToken: true);
                log($"Sign-in: token refreshed{(oauth.ExpiresUtc is { } until2 ? $", valid until {until2.ToLocalTime():HH:mm}" : string.Empty)}.");
                TokensChanged?.Invoke(config);
                return SecretStore.Unprotect(oauth.AccessToken);
            }
            catch (McpTokenException ex) when (ex.Error is "invalid_grant" or "invalid_client" or "unauthorized_client")
            {
                oauth.ClearTokens();
                TokensChanged?.Invoke(config);
                log("Sign-in: " + ex.Message + " Sign in again.");
                return null;
            }
            catch (McpException ex)
            {
                log("Sign-in: the token couldn't be refreshed: " + ex.Message);
                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// How early a token is renewed: <see cref="RefreshMargin"/>, or a tenth of its life for tokens shorter than ten
    /// minutes, so a short-lived token isn't renewed before every request.
    /// </summary>
    private static TimeSpan Margin(McpOAuthSettings oauth)
    {
        if (oauth.ObtainedUtc is not { } obtained || oauth.ExpiresUtc is not { } until)
            return RefreshMargin;
        var tenth = TimeSpan.FromTicks((until - obtained).Ticks / 10);
        return tenth < RefreshMargin ? tenth : RefreshMargin;
    }

    /// <summary>What to tell the person when a server needs signing in.</summary>
    public static string SignInNeeded(McpServerConfig config) => config.OAuth.Enabled
        ? $"{config.Name} needs you to sign in: Options › MCP servers › {config.Name} › Sign in."
        : $"{config.Name} asks for a sign-in. If it signs in through the browser, tick \"Sign in with the browser\" for it in Options › MCP servers and press Sign in; otherwise add an Authorization header.";

    // ---- The loopback callback ----------------------------------------------------------------------------------

    /// <summary>
    /// Listens on the callback address (127.0.0.1, and ::1 for localhost) until the browser arrives with the answer,
    /// answers it with a short page, and returns the query parameters.  Other requests (a favicon) get 404.
    /// </summary>
    private static async Task<Dictionary<string, string>> WaitForCallbackAsync(Uri callback, CancellationToken cancel)
    {
        var listeners = new List<TcpListener>();
        try
        {
            var addresses = callback.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                ? new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }
                : new[] { IPAddress.Parse(callback.Host.Trim('[', ']')) };
            foreach (var address in addresses)
            {
                var listener = new TcpListener(address, callback.Port);
                try
                {
                    listener.Start();
                    listeners.Add(listener);
                }
                catch (SocketException) when (listeners.Count > 0 || addresses.Length > 1 && address.Equals(IPAddress.IPv6Loopback))
                {
                }
            }
        }
        catch (SocketException ex)
        {
            foreach (var listener in listeners)
                listener.Stop();
            throw new McpException($"Port {callback.Port} on this computer is in use ({ex.SocketErrorCode}), so the browser can't come back to it. Choose another callback address (and register it with the app).");
        }
        if (listeners.Count == 0)
            throw new McpException($"Port {callback.Port} on this computer couldn't be opened for the sign-in.");

        using var stop = cancel.Register(() => listeners.ForEach(l => l.Stop()));
        try
        {
            var accepts = listeners.Select(l => l.AcceptTcpClientAsync(cancel).AsTask()).ToList();
            while (true)
            {
                var done = await Task.WhenAny(accepts);
                int which = accepts.IndexOf(done);
                using var client = await done;
                accepts[which] = listeners[which].AcceptTcpClientAsync(cancel).AsTask();
                var answer = await HandleCallbackAsync(client, callback.AbsolutePath, cancel);
                if (answer != null)
                    return answer;
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException && cancel.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancel);
        }
        finally
        {
            foreach (var listener in listeners)
                listener.Stop();
        }
    }

    private static async Task<Dictionary<string, string>?> HandleCallbackAsync(TcpClient client, string path, CancellationToken cancel)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(cancel);
        // Read and ignore the headers.
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancel)))
        {
        }
        var parts = requestLine?.Split(' ') ?? Array.Empty<string>();
        Uri? target = parts.Length >= 2 && Uri.TryCreate(new Uri("http://127.0.0.1"), parts[1], out var parsed) ? parsed : null;
        bool ours = parts.Length >= 2 && parts[0] == "GET" && target != null && target.AbsolutePath.Equals(path, StringComparison.Ordinal);
        Dictionary<string, string>? answer = null;
        string page;
        int status;
        if (ours)
        {
            answer = ParseQuery(target!.Query);
            status = 200;
            page = answer.ContainsKey("error")
                ? "<h2>Sign-in didn't complete</h2><p>The service said: " + WebUtility.HtmlEncode(answer["error"]) + ". You can close this tab and return to Mycelia.</p>"
                : "<h2>Signed in</h2><p>You can close this tab and return to Mycelia.</p>";
        }
        else
        {
            status = 404;
            page = "<p>Not found.</p>";
        }
        string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Mycelia</title></head><body style=\"font-family:Segoe UI,sans-serif;margin:3em\">" + page + "</body></html>";
        byte[] body = Encoding.UTF8.GetBytes(html);
        string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancel);
        await stream.WriteAsync(body, cancel);
        await stream.FlushAsync(cancel);
        return answer;
    }

    // ---- Small helpers ----------------------------------------------------------------------------------------

    public static HttpClient NewHttp() => new(new SocketsHttpHandler { ConnectTimeout = HttpTransport.ConnectTimeout }) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Base64url without padding (RFC 4648 section 5), as PKCE and the state need.</summary>
    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Form(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join("&", pairs.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = part.IndexOf('=');
            string key = Uri.UnescapeDataString((at < 0 ? part : part[..at]).Replace('+', ' '));
            string value = at < 0 ? string.Empty : Uri.UnescapeDataString(part[(at + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }
        return result;
    }

    private static JsonObject? TryParse(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

/// <summary>A token endpoint's error answer, with its OAuth error code.</summary>
public sealed class McpTokenException : McpException
{
    public McpTokenException(string error, string message) : base(message) => Error = error;
    public string Error { get; }
}
