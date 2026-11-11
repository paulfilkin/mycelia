using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Mcp;

/// <summary>How the app talks to an MCP server.</summary>
public enum McpTransportKind
{
    /// <summary>A local program started by the app; JSON-RPC messages one per line on its standard input and output.</summary>
    Stdio,
    /// <summary>A server at a web address (MCP Streamable HTTP).</summary>
    Http
}

/// <summary>Whether the assistant may use a tool: freely, after asking the person, or not at all.</summary>
public enum ToolPolicy
{
    Allow,
    Ask,
    Off
}

/// <summary>
/// One external MCP server, as kept in settings.json.  Environment and header values are stored encrypted
/// (SecretStore); <see cref="McpClient"/> decrypts them when it connects.  Ported from multifariousStudio without its
/// XLIFF round trip (TDD 22.5); browser sign-in was added in research slice C (TDD 22.5).
/// </summary>
public sealed class McpServerConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public McpTransportKind Transport { get; set; } = McpTransportKind.Stdio;

    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();

    public string Url { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Browser sign-in (OAuth) for a server at a web address; off unless switched on.</summary>
    public McpOAuthSettings OAuth { get; set; } = new();

    /// <summary>Seconds to wait for any one answer from the server.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>The policy for tools without one of their own.</summary>
    public ToolPolicy DefaultPolicy { get; set; } = ToolPolicy.Ask;
    public Dictionary<string, ToolPolicy> ToolPolicies { get; set; } = new();

    /// <summary>Tools the server marks read-only (annotations.readOnlyHint) are allowed without asking.</summary>
    public bool AllowReadOnlyTools { get; set; }

    public ToolPolicy PolicyFor(McpToolInfo tool) =>
        ToolPolicies.TryGetValue(tool.Name, out var policy) ? policy
        : AllowReadOnlyTools && tool.ReadOnly ? ToolPolicy.Allow
        : DefaultPolicy;

    public string Describe() => Transport == McpTransportKind.Http
        ? Url
        : string.Join(" ", new[] { Command }.Concat(Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

    public McpServerConfig Clone() =>
        JsonSerializer.Deserialize<McpServerConfig>(JsonSerializer.Serialize(this)) ?? new McpServerConfig();

    /// <summary>An id made of letters, digits, '-' and '_' (the start of each tool's name for the AI model).</summary>
    public static string MakeId(string name, IEnumerable<string> taken)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        string id = new string(chars).Trim('_');
        if (id.Length == 0)
            id = "server";
        if (id.Length > 20)
            id = id[..20];
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        string candidate = id;
        for (int i = 2; used.Contains(candidate); i++)
            candidate = $"{id}{i}";
        return candidate;
    }

    /// <summary>
    /// Reads the "mcpServers" object of a Claude Desktop, Claude Code, Cursor or VS Code configuration file
    /// (command/args/env for local servers, url/headers for remote ones).
    /// </summary>
    public static List<McpServerConfig> ImportFromJson(string json, IEnumerable<string> takenIds)
    {
        var result = new List<McpServerConfig>();
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        var servers = (root?["mcpServers"] ?? root?["servers"] ?? root?["mcp"]?["servers"]) as JsonObject;
        if (servers == null)
            return result;
        var taken = takenIds.ToList();
        foreach (var (name, node) in servers)
        {
            if (node is not JsonObject entry)
                continue;
            var config = new McpServerConfig { Name = name, Id = MakeId(name, taken) };
            taken.Add(config.Id);
            if (entry["url"]?.GetValue<string>() is { Length: > 0 } url)
            {
                config.Transport = McpTransportKind.Http;
                config.Url = url;
                if (entry["headers"] is JsonObject headers)
                    foreach (var (key, value) in headers)
                        config.Headers[key] = value?.ToString() ?? string.Empty;
            }
            else
            {
                config.Command = entry["command"]?.GetValue<string>() ?? string.Empty;
                if (entry["args"] is JsonArray args)
                    config.Arguments = args.Select(a => a?.ToString() ?? string.Empty).ToList();
                if (entry["env"] is JsonObject env)
                    foreach (var (key, value) in env)
                        config.Environment[key] = value?.ToString() ?? string.Empty;
                config.WorkingDirectory = entry["cwd"]?.GetValue<string>();
            }
            if (entry["disabled"]?.GetValue<bool>() == true)
                config.Enabled = false;
            result.Add(config);
        }
        return result;
    }
}

/// <summary>
/// How a server at a web address is signed in to through the browser, following the MCP authorization rules
/// (2025-06-18): OAuth 2.1 authorization code with PKCE, the server's protected resource metadata (RFC 9728) naming
/// its authorization server, that server's metadata (RFC 8414), and a client that is either the person's own app
/// (client ID and secret from the service's developer console) or one registered automatically (RFC 7591) where the
/// service allows it.  Secrets and tokens are kept encrypted (SecretStore).  Ported from multifariousStudio.
/// </summary>
public sealed class McpOAuthSettings
{
    /// <summary>Port 47822, beside Mycelia's own server (47821) and apart from multifariousStudio's, so both can sign in.</summary>
    public const string DefaultCallback = "http://127.0.0.1:47822/callback";

    public bool Enabled { get; set; }

    /// <summary>The person's own app's client ID; empty registers one automatically if the service allows it.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The own app's client secret, encrypted; empty for a public client (PKCE only).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Where the browser comes back to: a loopback address Mycelia listens on while signing in.</summary>
    public string CallbackAddress { get; set; } = DefaultCallback;

    /// <summary>Space-separated scopes to ask for; empty asks for what the server says it needs.</summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>Extra parameters for the sign-in address, such as token_access_type=offline (some services' refresh tokens).</summary>
    public Dictionary<string, string> ExtraParameters { get; set; } = new();

    /// <summary>Send the server's address as the resource parameter (RFC 8707), as the MCP rules require.</summary>
    public bool SendResource { get; set; } = true;

    // Learnt while signing in, and the tokens.
    public string Issuer { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string TokenAuthMethod { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string RegisteredClientId { get; set; } = string.Empty;
    public string RegisteredClientSecret { get; set; } = string.Empty;
    public string RegisteredForCallback { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime? ObtainedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public string GrantedScopes { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSignedIn => AccessToken.Length > 0;

    /// <summary>Forgets the tokens (signing out); the app details stay.</summary>
    public void ClearTokens()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        ObtainedUtc = null;
        ExpiresUtc = null;
        GrantedScopes = string.Empty;
    }

    /// <summary>Copies what signing in and refreshing produced (endpoints, registration, tokens) from another copy.</summary>
    public void TakeTokensFrom(McpOAuthSettings other)
    {
        Issuer = other.Issuer;
        AuthorizationEndpoint = other.AuthorizationEndpoint;
        TokenEndpoint = other.TokenEndpoint;
        TokenAuthMethod = other.TokenAuthMethod;
        Resource = other.Resource;
        RegisteredClientId = other.RegisteredClientId;
        RegisteredClientSecret = other.RegisteredClientSecret;
        RegisteredForCallback = other.RegisteredForCallback;
        AccessToken = other.AccessToken;
        RefreshToken = other.RefreshToken;
        ObtainedUtc = other.ObtainedUtc;
        ExpiresUtc = other.ExpiresUtc;
        GrantedScopes = other.GrantedScopes;
    }
}

/// <summary>A tool an MCP server offers (tools/list).</summary>
public sealed class McpToolInfo
{
    public string Name { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public JsonObject InputSchema { get; init; } = new() { ["type"] = "object", ["properties"] = new JsonObject() };

    /// <summary>annotations.readOnlyHint: the tool says it changes nothing.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>annotations.destructiveHint: the tool may delete or overwrite.</summary>
    public bool Destructive { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Title) ? Name : $"{Title} ({Name})";
}

/// <summary>A tool's answer (tools/call): its text content joined, and whether it reported an error.</summary>
public sealed record McpCallResult(string Text, bool IsError);

public class McpException : Exception
{
    public McpException(string message, int? code = null) : base(message) => Code = code;
    public int? Code { get; }
}
