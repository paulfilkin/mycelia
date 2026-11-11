using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mycelia.Ai.Mcp;

/// <summary>How a server at a web address answered a first request without credentials.</summary>
public enum ProbeKind
{
    /// <summary>It answered the MCP initialise: no sign-in needed.</summary>
    Open,
    /// <summary>401 naming its resource metadata: browser sign-in (OAuth).</summary>
    BrowserSignIn,
    /// <summary>401 or 403 without metadata: a key or token in a header.</summary>
    NeedsToken,
    /// <summary>It answered, but not as an MCP server (a web page, for example).</summary>
    NotMcp,
    /// <summary>No answer.</summary>
    Unreachable
}

public sealed record ProbeResult(ProbeKind Kind, string Detail, string? RegistrationEndpoint = null, string? Issuer = null, string? Scopes = null);

/// <summary>Something the helper must ask the person, and where the answer goes.</summary>
public sealed record SetupQuestion(string Key, string Label, string? Help, bool Secret, bool Required, string? Default);

/// <summary>
/// What the MCP Helper will put into a server's settings: the connection (web address or command), the headers,
/// environment values and arguments as templates with {placeholders} filled from the person's answers, whether to
/// sign in through the browser, and notes explaining it. Built from an MCP Registry entry, a probe of a web address,
/// or a pasted configuration.
/// </summary>
public sealed class SetupPlan
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public McpTransportKind Transport { get; set; } = McpTransportKind.Http;
    public string Url { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; } = new();
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);
    public bool BrowserSignIn { get; set; }
    public Dictionary<string, string> OAuthParameters { get; } = new();
    public List<SetupQuestion> Questions { get; } = new();
    public List<string> Notes { get; } = new();

    /// <summary>A question for each {placeholder}; one already asked keeps its first wording.</summary>
    private void Ask(SetupQuestion question)
    {
        if (Questions.All(q => q.Key != question.Key))
            Questions.Add(question);
    }

    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_.\-]*)\}");

    /// <summary>Adds questions for the {placeholders} in a template, described by the input it belongs to.</summary>
    private void AskFor(string template, RegistryInput input, string where)
    {
        foreach (Match match in Placeholder.Matches(template))
        {
            string key = match.Groups[1].Value;
            // A placeholder in a value has to be filled, whatever the input says about itself.
            Ask(new SetupQuestion(key, Readable(key), input.Description ?? where, input.Secret || LooksSecret(key), true, input.Default));
        }
    }

    /// <summary>"smithery_api_key" → "Smithery API key".</summary>
    public static string Readable(string key)
    {
        var words = Regex.Split(key.Replace('-', '_').Replace('.', '_'), "_+").Where(w => w.Length > 0)
            .Select(w => w.ToLowerInvariant() switch { "api" => "API", "url" => "URL", "id" => "ID", "mcp" => "MCP", "oauth" => "OAuth", _ => w.ToLowerInvariant() })
            .ToList();
        if (words.Count == 0)
            return key;
        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(' ', words);
    }

    private static bool LooksSecret(string name) =>
        Regex.IsMatch(name, "(token|secret|key|password|pat|credential|auth)", RegexOptions.IgnoreCase);

    // ---- From the MCP Registry ----------------------------------------------------------------------

    public static SetupPlan FromRegistry(RegistryServer server)
    {
        var plan = new SetupPlan
        {
            Name = server.FoundAtOwnAddress ? server.DisplayName.Replace(" (its own address)", string.Empty) : server.DisplayName,
            Source = server.FoundAtOwnAddress ? "Found at the company's own MCP address" : $"MCP Registry: {server.Name}{(server.Version != null ? " " + server.Version : string.Empty)}"
        };
        var remote = server.Remotes.FirstOrDefault(r => r.Type == "streamable-http") ?? server.Remotes.FirstOrDefault(r => r.Type != "sse");
        var sseOnly = remote == null ? server.Remotes.FirstOrDefault() : null;
        if (remote != null)
        {
            plan.Transport = McpTransportKind.Http;
            plan.Url = remote.Url;
            foreach (var variable in remote.UrlVariables)
                plan.Ask(new SetupQuestion(variable.Name, Readable(variable.Name), variable.Description ?? "Part of the server's address.", variable.Secret, variable.Required, variable.Default));
            foreach (var header in remote.Headers)
            {
                if (header.Value == null && header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    // An Authorization header with no stated format takes a bearer token (RFC 6750); the person pastes
                    // the token alone, and a value they paste with its own scheme (Bearer, Basic) is kept as it is.
                    plan.Headers[header.Name] = "Bearer {access_token}";
                    plan.Ask(new SetupQuestion("access_token", "Access token", (header.Description ?? "The token the service gives you.") + " Paste the token itself; \"Bearer \" is added.",
                        true, true, header.Default));
                    continue;
                }
                string template = header.Value ?? $"{{{HeaderKey(header.Name)}}}";
                plan.Headers[header.Name] = template;
                if (header.Value == null)
                    plan.Ask(new SetupQuestion(HeaderKey(header.Name), header.Name, header.Description ?? $"The value of the {header.Name} header.", header.Secret || LooksSecret(header.Name), header.Required, header.Default));
                else
                    plan.AskFor(template, header, $"Goes into the {header.Name} header.");
            }
            plan.Notes.Add($"Connects to {remote.Url} over the web (Streamable HTTP).");
            return plan;
        }
        var package = server.Packages.FirstOrDefault(p => p.Transport == "stdio") ?? server.Packages.FirstOrDefault();
        if (package != null)
        {
            plan.Transport = McpTransportKind.Stdio;
            plan.AddPackage(package);
            if (package.Transport != "stdio")
                plan.Notes.Add($"This package serves over {package.Transport}; Mycelia starts it as a program and talks to it directly, which may not work. Check its documentation.");
            return plan;
        }
        if (sseOnly != null)
        {
            plan.Url = sseOnly.Url;
            plan.Notes.Add($"This server only offers the older SSE connection ({sseOnly.Url}), which Mycelia doesn't support. Look for a Streamable HTTP address in its documentation (often the same address ending in /mcp instead of /sse).");
            return plan;
        }
        plan.Notes.Add("The registry gives no address or package for this server: see its documentation.");
        return plan;
    }

    private static string HeaderKey(string header) => Regex.Replace(header.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    private void AddPackage(RegistryPackage package)
    {
        string version = package.Version is { Length: > 0 } v && v != "latest" ? v : string.Empty;
        switch (package.RegistryType.ToLowerInvariant())
        {
            case "pypi":
                Command = package.RuntimeHint ?? "uvx";
                Arguments.Add(package.Identifier + (version.Length > 0 ? "@" + version : string.Empty));
                break;
            case "oci":
            case "docker":
                Command = package.RuntimeHint ?? "docker";
                Arguments.AddRange(new[] { "run", "-i", "--rm" });
                foreach (var variable in package.Environment)
                    Arguments.AddRange(new[] { "-e", variable.Name });
                Arguments.Add(package.Identifier.Contains(':') || version.Length == 0 ? package.Identifier : package.Identifier + ":" + version);
                break;
            case "nuget":
                Command = package.RuntimeHint ?? "dnx";
                Arguments.Add(package.Identifier + (version.Length > 0 ? "@" + version : string.Empty));
                Arguments.Add("--yes");
                break;
            default:
                Command = package.RuntimeHint ?? "npx";
                Arguments.Add("-y");
                Arguments.Add(package.Identifier + (version.Length > 0 ? "@" + version : string.Empty));
                break;
        }
        foreach (var argument in package.Arguments)
        {
            string value = argument.Input.Value ?? argument.Input.Default ?? $"{{{HeaderKey(argument.Name ?? argument.Input.Name)}}}";
            if (argument.Type == "named" && argument.Name is { } name)
                Arguments.Add(name.StartsWith('-') ? name : "--" + name);
            Arguments.Add(value);
            if (argument.Input.Value == null && argument.Input.Default == null)
                Ask(new SetupQuestion(HeaderKey(argument.Name ?? argument.Input.Name), Readable(argument.Name ?? argument.Input.Name), argument.Input.Description, argument.Input.Secret, argument.Input.Required, null));
            else
                AskFor(value, argument.Input, "A value the program is started with.");
        }
        foreach (var variable in package.Environment)
        {
            string template = variable.Value ?? $"{{{variable.Name}}}";
            Environment[variable.Name] = template;
            if (variable.Value == null)
                Ask(new SetupQuestion(variable.Name, variable.Name, variable.Description, variable.Secret || LooksSecret(variable.Name), variable.Required, variable.Default));
            else
                AskFor(template, variable, $"Goes into the {variable.Name} setting.");
        }
        Notes.Add($"Runs on this computer: {Command} {string.Join(' ', Arguments.Where(a => !a.StartsWith('{')))}.");
        Notes.Add(Prerequisite(Command));
    }

    /// <summary>Whether the program that starts the server is installed, and if not where to get it.</summary>
    public static string Prerequisite(string command)
    {
        var (found, _) = StdioTransport.ResolveCommand(command);
        bool installed = !string.Equals(found, command, StringComparison.OrdinalIgnoreCase) || File.Exists(command);
        string what = command.ToLowerInvariant() switch
        {
            "npx" or "node" or "npm" => "Node.js (nodejs.org, the LTS installer)",
            "uvx" or "uv" => "uv (docs.astral.sh/uv, one PowerShell command)",
            "docker" => "Docker Desktop (docker.com)",
            "dnx" or "dotnet" => "the .NET 10 SDK (dot.net)",
            "python" or "py" => "Python (python.org)",
            _ => command
        };
        return installed ? $"{command} is installed on this computer." : $"{command} isn't installed on this computer: install {what} first, then start Mycelia again.";
    }

    // ---- From a web address ---------------------------------------------------------------------------

    public static SetupPlan FromUrl(string url)
    {
        var uri = new Uri(url.Trim());
        var plan = new SetupPlan { Name = NameFromHost(uri), Source = "The address you gave", Transport = McpTransportKind.Http, Url = uri.AbsoluteUri };
        plan.Notes.Add($"Connects to {uri.AbsoluteUri} over the web (Streamable HTTP).");
        return plan;
    }

    private static string NameFromHost(Uri uri)
    {
        var parts = uri.Host.Split('.').Where(p => p is not ("www" or "mcp" or "api" or "com" or "io" or "ai" or "net" or "org" or "app" or "dev")).ToList();
        string name = parts.Count > 0 ? parts[0] : uri.Host;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>A pasted mcpServers configuration (Claude Desktop, Cursor, VS Code); values that look like placeholders become questions.</summary>
    public static SetupPlan? FromConfig(string json)
    {
        List<McpServerConfig> servers;
        try { servers = McpServerConfig.ImportFromJson(json.Trim().StartsWith('"') ? "{" + json + "}" : json, Array.Empty<string>()); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
        if (servers.Count == 0)
            return null;
        var config = servers[0];
        var plan = new SetupPlan
        {
            Name = config.Name, Source = "The configuration you pasted", Transport = config.Transport, Url = config.Url, Command = config.Command
        };
        plan.Arguments.AddRange(config.Arguments.Select(a => PlaceholderToTemplate(a, plan)));
        foreach (var (key, value) in config.Headers)
            plan.Headers[key] = PlaceholderToTemplate(value, plan, key);
        foreach (var (key, value) in config.Environment)
            plan.Environment[key] = PlaceholderToTemplate(value, plan, key);
        plan.Notes.Add(config.Transport == McpTransportKind.Http ? $"Connects to {config.Url} over the web." : $"Runs on this computer: {config.Command} {string.Join(' ', config.Arguments)}.");
        if (config.Transport == McpTransportKind.Stdio && config.Command.Length > 0)
            plan.Notes.Add(Prerequisite(config.Command));
        if (servers.Count > 1)
            plan.Notes.Add($"The configuration has {servers.Count} servers; this is the first, {config.Name}. Use Import on Options › MCP servers for all of them.");
        return plan;
    }

    /// <summary>"&lt;YOUR_TOKEN&gt;", "${GITHUB_TOKEN}", "your-api-key"... become a {placeholder} with a question.</summary>
    private static string PlaceholderToTemplate(string value, SetupPlan plan, string? name = null)
    {
        var match = Regex.Match(value, @"<([^<>]{2,60})>|\$\{([A-Za-z_][A-Za-z0-9_]*)\}|\b(your[-_ ][A-Za-z0-9 _-]{2,40}|YOUR_[A-Z0-9_]+|xxx+|\.\.\.)\b");
        if (!match.Success)
            return value;
        string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        string key = HeaderKey(raw.StartsWith("your", StringComparison.OrdinalIgnoreCase) ? raw[4..] : raw);
        if (key.Length == 0)
            key = HeaderKey(name ?? "value");
        plan.Ask(new SetupQuestion(key, Readable(key), name != null ? $"Goes into {name}." : null, LooksSecret(key) || LooksSecret(name ?? string.Empty), true, null));
        return value[..match.Index] + "{" + key + "}" + value[(match.Index + match.Length)..];
    }

    // ---- What the address said ------------------------------------------------------------------------

    /// <summary>Adds what a probe found: browser sign-in (and whether an own app is needed), a token, or a problem.</summary>
    public void Apply(ProbeResult probe)
    {
        bool hasAuthHeader = Headers.Keys.Any(k => k.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || k.Contains("key", StringComparison.OrdinalIgnoreCase) || k.Contains("token", StringComparison.OrdinalIgnoreCase));
        switch (probe.Kind)
        {
            case ProbeKind.Open:
                Notes.Add("The address answered without a sign-in.");
                break;
            case ProbeKind.BrowserSignIn when !hasAuthHeader:
                BrowserSignIn = true;
                Notes.Add($"It signs in through the browser ({probe.Issuer ?? "its sign-in service"}): after filling in, press Sign in and approve Mycelia there.");
                if (probe.RegistrationEndpoint == null)
                {
                    Notes.Add("Its sign-in service doesn't register apps automatically, so you need an app of your own there: create one in the service's developer settings, add the callback address shown below as its redirect address, and enter its client ID (and secret).");
                    Ask(new SetupQuestion("oauth_client_id", "Client ID of your app", "From the app you created in the service's developer settings.", false, true, null));
                    Ask(new SetupQuestion("oauth_client_secret", "Client secret of your app", "Leave empty if the app has none.", true, false, null));
                }
                else
                {
                    Notes.Add("Its sign-in service can register Mycelia automatically. If Sign in says it won't (some services only allow apps they know), create an app of your own there and enter its client ID and secret in the Client ID and Client secret boxes.");
                }
                if (probe.Issuer?.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Dropbox gives a refresh token only when asked (token_access_type=offline).
                    OAuthParameters["token_access_type"] = "offline";
                    Notes.Add("Dropbox only keeps a sign-in going when asked to (token_access_type=offline); that's added for you.");
                }
                break;
            case ProbeKind.BrowserSignIn:
                Notes.Add("It can also sign in through the browser; this set-up uses the token below instead.");
                break;
            case ProbeKind.NeedsToken when !hasAuthHeader:
                Notes.Add("It asks for a key or token. Create one in the service's account or developer settings, then enter it below.");
                Headers["Authorization"] = "Bearer {access_token}";
                Ask(new SetupQuestion("access_token", "Access token or API key", "Sent as Authorization: Bearer <token>. If the service's documentation names another header, change it on Options › MCP servers after filling in.", true, true, null));
                break;
            case ProbeKind.NotMcp:
                Notes.Add("The address didn't answer as an MCP server: " + probe.Detail + " It may be a documentation page rather than the server's address.");
                break;
            case ProbeKind.Unreachable:
                Notes.Add("The address couldn't be reached: " + probe.Detail);
                break;
        }
    }

    // ---- Filling in ----------------------------------------------------------------------------------------

    /// <summary>The required questions not answered.</summary>
    public List<SetupQuestion> Missing(IReadOnlyDictionary<string, string> answers) =>
        Questions.Where(q => q.Required && (!answers.TryGetValue(q.Key, out var a) || a.Trim().Length == 0) && string.IsNullOrEmpty(q.Default)).ToList();

    private string Fill(string template, IReadOnlyDictionary<string, string> answers) =>
        Placeholder.Replace(template, m =>
        {
            string key = m.Groups[1].Value;
            if (answers.TryGetValue(key, out var answer) && answer.Trim().Length > 0)
                return answer.Trim();
            return Questions.FirstOrDefault(q => q.Key == key)?.Default ?? string.Empty;
        });

    /// <summary>A header's value; an Authorization answer that already has its scheme ("Bearer ...", "Basic ...") isn't given a second one.</summary>
    private string FillHeader(string name, string template, IReadOnlyDictionary<string, string> answers)
    {
        string value = Fill(template, answers);
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            var doubled = Regex.Match(value, @"^(Bearer|Basic|Token)\s+((?:Bearer|Basic|Token)\s+.+)$", RegexOptions.IgnoreCase);
            if (doubled.Success)
                value = doubled.Groups[2].Value;
        }
        return value;
    }

    /// <summary>
    /// Writes the plan into a server's settings (secret values encrypted): name, connection, headers, environment,
    /// arguments and browser sign-in. Headers and settings left empty are dropped.
    /// </summary>
    public void FillIn(McpServerConfig config, IReadOnlyDictionary<string, string> answers)
    {
        if (Name.Length > 0)
            config.Name = Name;
        config.Transport = Transport;
        if (Transport == McpTransportKind.Http)
        {
            config.Url = Fill(Url, answers);
            config.Headers = Headers.Select(p => (p.Key, Value: FillHeader(p.Key, p.Value, answers)))
                .Where(p => p.Value.Trim().Length > 0 && p.Value.Trim() != "Bearer")
                .ToDictionary(p => p.Key, p => SecretStore.Protect(p.Value), StringComparer.Ordinal);
            config.OAuth.Enabled = BrowserSignIn;
            if (BrowserSignIn)
            {
                if (answers.TryGetValue("oauth_client_id", out var clientId) && clientId.Trim().Length > 0)
                    config.OAuth.ClientId = clientId.Trim();
                if (answers.TryGetValue("oauth_client_secret", out var secret) && secret.Trim().Length > 0)
                    config.OAuth.ClientSecret = SecretStore.Protect(secret.Trim());
                foreach (var (key, value) in OAuthParameters)
                    config.OAuth.ExtraParameters[key] = value;
            }
        }
        else
        {
            config.Command = Command;
            config.Arguments = Arguments.Select(a => Fill(a, answers)).Where(a => a.Length > 0).ToList();
            config.Environment = Environment.Select(p => (p.Key, Value: Fill(p.Value, answers)))
                .Where(p => p.Value.Length > 0).ToDictionary(p => p.Key, p => SecretStore.Protect(p.Value), StringComparer.Ordinal);
        }
    }

    /// <summary>The plan as text for the AI's explanation (no answers, so no secrets).</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"Name: {Name} ({Source})");
        text.AppendLine(Transport == McpTransportKind.Http ? $"Web address: {Url}" : $"Command: {Command} {string.Join(' ', Arguments)}");
        foreach (var (key, value) in Headers)
            text.AppendLine($"Header {key}: {value}");
        foreach (var (key, value) in Environment)
            text.AppendLine($"Environment {key}={value}");
        if (BrowserSignIn)
            text.AppendLine("Browser sign-in (OAuth) on.");
        foreach (var question in Questions)
            text.AppendLine($"Question {question.Key}: {question.Label}{(question.Help != null ? " - " + question.Help : string.Empty)}");
        foreach (string note in Notes)
            text.AppendLine("Note: " + note);
        return text.ToString();
    }
}

/// <summary>Asks a web address what kind of server it is, without credentials, and reads its sign-in details.</summary>
public static class McpProbe
{
    public static async Task<ProbeResult> ProbeAsync(HttpClient http, string url, CancellationToken cancel)
    {
        var config = new McpServerConfig { Name = "probe", Transport = McpTransportKind.Http, Url = url.Trim() };
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url.Trim());
            request.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"" + McpClient.ProtocolVersion +
                "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"" + McpClient.ClientName + "\",\"version\":\"1\"}}}", new UTF8Encoding(false), "application/json");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return new ProbeResult(ProbeKind.Unreachable, ex.Message);
        }
        using (response)
        {
            string type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (response.IsSuccessStatusCode)
            {
                if (type.Contains("event-stream"))
                    return new ProbeResult(ProbeKind.Open, "It answered with an event stream.");
                string body = await ReadSome(response, cancel);
                return body.Contains("\"jsonrpc\"") ? new ProbeResult(ProbeKind.Open, "It answered the MCP initialise.")
                    : new ProbeResult(ProbeKind.NotMcp, type.Contains("html") ? "It answered with a web page." : $"It answered {(int)response.StatusCode} with {type}.");
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized && McpOAuth.ChallengeParameters(response).ContainsKey("resource_metadata"))
            {
                try
                {
                    var found = await McpOAuth.DiscoverAsync(http, config, _ => { }, cancel);
                    return new ProbeResult(ProbeKind.BrowserSignIn, "It asked for a browser sign-in.", found.RegistrationEndpoint, found.Issuer, found.Scope);
                }
                catch (McpException ex)
                {
                    return new ProbeResult(ProbeKind.BrowserSignIn, ex.Message);
                }
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new ProbeResult(ProbeKind.NeedsToken, $"It answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound)
                return new ProbeResult(ProbeKind.NotMcp, $"It answered {(int)response.StatusCode} {response.ReasonPhrase} to an MCP request.");
            return new ProbeResult(ProbeKind.NotMcp, $"It answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static async Task<string> ReadSome(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var buffer = new byte[8192];
        int read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancel);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}

/// <summary>Documentation text for the helper's explanation: a web page as plain text, or a GitHub repository's README.</summary>
public static class McpDocs
{
    public const int MaxChars = 12000;

    public static async Task<string?> FetchAsync(HttpClient http, string url, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(url, cancel);
            if (!response.IsSuccessStatusCode)
                return null;
            string text = await response.Content.ReadAsStringAsync(cancel);
            bool html = response.Content.Headers.ContentType?.MediaType?.Contains("html") == true || text.TrimStart().StartsWith('<');
            text = html ? HtmlToText(text) : text;
            return text.Length > MaxChars ? text[..MaxChars] : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A GitHub repository's README as raw text (with its subfolder), or null for other hosts.</summary>
    public static string? ReadmeUrl(string? repository, string? subfolder)
    {
        if (repository == null || !Uri.TryCreate(repository, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            return null;
        string repo = parts[1].EndsWith(".git") ? parts[1][..^4] : parts[1];
        string folder = string.IsNullOrWhiteSpace(subfolder) ? string.Empty : subfolder.Trim('/') + "/";
        return $"https://raw.githubusercontent.com/{parts[0]}/{repo}/HEAD/{folder}README.md";
    }

    /// <summary>A web page as plain text: the same conversion fetch_url uses (Research.WebFetch).</summary>
    public static string HtmlToText(string html) => Research.WebFetch.HtmlToText(html);

    /// <summary>The system prompt for the helper's explanation.</summary>
    public const string ExplainPrompt =
        "You help someone researching their family history, who isn't technical, connect an MCP server to Mycelia, a Windows family tree program. You get the server's registry entry or address, " +
        "what Mycelia found when it contacted the address, the settings it will fill in, the questions it will ask, and documentation text. " +
        "The window is called MCP Helper; its questions are under 'Details to enter' and its button is 'Fill in the settings', which fills in Options › MCP servers. " +
        "Write short, numbered steps in British English Markdown:\n" +
        "1. What to do in the product first (sign up, plan or admin approval needed, where to create the API token, personal access token or app, which permissions or scopes to tick), naming the menus to click.\n" +
        "2. What to type into each question Mycelia asks, and where that value comes from.\n" +
        "3. After Fill in the settings: press OK, or Test connection; for browser sign-in, press Sign in first.\n" +
        "Only use what the documentation or your sure knowledge says; when unsure, say so and point to the product's documentation. Never invent web addresses. " +
        "Don't repeat the settings back in full. At most about 250 words. If anything else must be decided first, end with one short question to the person.";
}
