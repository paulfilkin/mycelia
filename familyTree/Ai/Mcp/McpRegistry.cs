using System.Net.Http;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Mcp;

/// <summary>A value a server needs: a header, an environment variable, an address variable or an argument.</summary>
public sealed record RegistryInput(string Name, string? Description, bool Required, bool Secret, string? Value, string? Default, string? Format);

/// <summary>A way to reach a server over the web (registry "remotes").</summary>
public sealed record RegistryRemote(string Type, string Url, IReadOnlyList<RegistryInput> Headers, IReadOnlyList<RegistryInput> UrlVariables);

/// <summary>A program to run on this computer (registry "packages"): npm, PyPI, a container image, NuGet...</summary>
public sealed record RegistryPackage(string RegistryType, string Identifier, string? Version, string? RuntimeHint, string Transport,
    IReadOnlyList<RegistryInput> Environment, IReadOnlyList<RegistryArgument> Arguments);

/// <summary>A package argument: positional (just the value) or named (--name value).</summary>
public sealed record RegistryArgument(string Type, string? Name, RegistryInput Input);

/// <summary>One server from the MCP Registry (server.json).</summary>
public sealed record RegistryServer(string Name, string? Title, string Description, string? Version, string? WebsiteUrl, string? RepositoryUrl,
    string? RepositorySubfolder, IReadOnlyList<RegistryRemote> Remotes, IReadOnlyList<RegistryPackage> Packages)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Name : Title!;

    /// <summary>Who published it: the namespace before "/" (reverse domain, or io.github.user).</summary>
    public string Publisher => Name.Contains('/') ? Name[..Name.IndexOf('/')] : Name;

    /// <summary>Found at the company's own MCP address rather than in the registry.</summary>
    public bool FoundAtOwnAddress { get; init; }

    /// <summary>
    /// Published by the company the person named: its namespace is that company's domain (com.canva.mcp for "canva") or
    /// its GitHub organisation (io.github.github for "github"), or it was found at the company's own address.
    /// </summary>
    public bool IsFrom(string company)
    {
        if (FoundAtOwnAddress)
            return true;
        string word = new string(company.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (word.Length == 0)
            return false;
        var parts = Publisher.ToLowerInvariant().Split('.');
        if (parts.Length >= 2 && parts[0] == "io" && parts[1] == "github")
            return parts.Length >= 3 && parts[2] == word;
        return parts.Length >= 2 && parts[1] == word;
    }

    /// <summary>"Web address" or the package kind, for lists.</summary>
    public string Kind => Remotes.Count > 0 ? "Web address" : Packages.Count > 0 ? $"Runs here ({Packages[0].RegistryType})" : "No connection details";
}

/// <summary>
/// The official MCP Registry (registry.modelcontextprotocol.io, API v0): servers with their web addresses (remotes,
/// with the headers and address variables they need) and packages (with environment variables and arguments). Only the
/// latest version of each is asked for.
/// </summary>
public static class McpRegistry
{
    public const string BaseUrl = "https://registry.modelcontextprotocol.io";

    public static async Task<List<RegistryServer>> SearchAsync(HttpClient http, string query, int limit, CancellationToken cancel)
    {
        string url = $"{BaseUrl}/v0/servers?search={Uri.EscapeDataString(query.Trim())}&version=latest&limit={Math.Clamp(limit, 1, 100)}";
        using var response = await http.GetAsync(url, cancel);
        if (!response.IsSuccessStatusCode)
            throw new McpException($"The MCP Registry answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        return Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>
    /// Finds a server by a name the person typed. The registry's search matches part of the server's id, and popular
    /// names have many community servers ahead of the official one (100+ for "github"), so the official naming
    /// patterns are searched too: name, name-mcp-server, name/name (io.github.github/github-mcp-server), com.name
    /// (com.canva.mcp/mcp), io.github.name. The answers are joined, ranked with <see cref="Rank"/>.
    /// </summary>
    public static async Task<List<RegistryServer>> FindAsync(HttpClient http, string query, CancellationToken cancel)
    {
        string q = query.Trim();
        var searches = new List<string> { q };
        if (!q.Contains(' ') && !q.Contains('/'))
        {
            string word = q.ToLowerInvariant();
            searches.AddRange(new[] { word + "-mcp-server", word + "/" + word, "com." + word, "io.github." + word });
        }
        var tasks = searches.Select(async s =>
        {
            try { return await SearchAsync(http, s, 30, cancel); }
            catch (Exception ex) when (ex is McpException or HttpRequestException or System.Text.Json.JsonException) { return new List<RegistryServer>(); }
        }).ToList();
        var own = OwnAddressAsync(http, q, cancel);
        var all = (await Task.WhenAll(tasks)).SelectMany(list => list).GroupBy(s => s.Name).Select(g => g.First()).ToList();
        if (await own is { } found && all.All(s => s.Remotes.All(r => !SameAddress(r.Url, found.Remotes[0].Url))))
            all.Insert(0, found);
        if (all.Count == 0)
            await SearchAsync(http, q, 30, cancel); // Lets an unreachable registry say so.
        return Rank(all, q);
    }

    private static bool SameAddress(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Many companies serve their MCP server at https://mcp.&lt;name&gt;.com/mcp (Canva, Dropbox, Notion...), and not all
    /// of them are in the registry. That address is tried, and offered only when it answers as an MCP server (it signs
    /// in through the browser or needs nothing); a plain 401 or a web page isn't taken as one.
    /// </summary>
    public static async Task<RegistryServer?> OwnAddressAsync(HttpClient http, string company, CancellationToken cancel)
    {
        string word = company.Trim().ToLowerInvariant();
        if (word.Length < 2 || !word.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return null;
        string url = $"https://mcp.{word}.com/mcp";
        var probe = await McpProbe.ProbeAsync(http, url, cancel);
        if (probe.Kind is not (ProbeKind.Open or ProbeKind.BrowserSignIn))
            return null;
        string title = char.ToUpperInvariant(word[0]) + word[1..];
        return new RegistryServer($"mcp.{word}.com", $"{title} (its own address)", $"Found at {url}, {title}'s own MCP address; not listed in the MCP Registry.",
            null, $"https://{word}.com", null, null, new[] { new RegistryRemote("streamable-http", url, Array.Empty<RegistryInput>(), Array.Empty<RegistryInput>()) },
            Array.Empty<RegistryPackage>()) { FoundAtOwnAddress = true };
    }

    /// <summary>The "servers" of a registry answer, newest version of each name only.</summary>
    public static List<RegistryServer> Parse(string json)
    {
        var result = new List<RegistryServer>();
        if (JsonNode.Parse(json)?["servers"] is not JsonArray servers)
            return result;
        foreach (var item in servers.OfType<JsonObject>())
        {
            var server = item["server"] as JsonObject ?? item;
            var official = item["_meta"]?["io.modelcontextprotocol.registry/official"];
            if (official?["isLatest"] is JsonValue latest && latest.TryGetValue(out bool isLatest) && !isLatest)
                continue;
            if (official?["status"]?.GetValue<string>() is { } status && status != "active")
                continue;
            result.Add(ParseServer(server));
        }
        return result.GroupBy(s => s.Name).Select(g => g.Last()).ToList();
    }

    public static RegistryServer ParseServer(JsonObject server) => new(
        Str(server, "name") ?? "(no name)",
        Str(server, "title"),
        Str(server, "description") ?? string.Empty,
        Str(server, "version"),
        Str(server, "websiteUrl"),
        Str(server["repository"] as JsonObject, "url"),
        Str(server["repository"] as JsonObject, "subfolder"),
        (server["remotes"] as JsonArray)?.OfType<JsonObject>().Select(r => new RegistryRemote(
            Str(r, "type") ?? "streamable-http",
            Str(r, "url") ?? string.Empty,
            Inputs(r["headers"]),
            Variables(r["variables"]))).Where(r => r.Url.Length > 0).ToList() ?? new List<RegistryRemote>(),
        (server["packages"] as JsonArray)?.OfType<JsonObject>().Select(p => new RegistryPackage(
            Str(p, "registryType") ?? Str(p, "registry_type") ?? "npm",
            Str(p, "identifier") ?? Str(p, "name") ?? string.Empty,
            Str(p, "version"),
            Str(p, "runtimeHint"),
            Str(p["transport"] as JsonObject, "type") ?? "stdio",
            Inputs(p["environmentVariables"]),
            Arguments(p["packageArguments"]))).Where(p => p.Identifier.Length > 0).ToList() ?? new List<RegistryPackage>());

    /// <summary>Closest first: the official one (its name or title is the query), then title words, then name.</summary>
    public static List<RegistryServer> Rank(IEnumerable<RegistryServer> servers, string query)
    {
        string q = query.Trim().ToLowerInvariant();
        int Score(RegistryServer s)
        {
            string title = (s.Title ?? string.Empty).ToLowerInvariant();
            string name = s.Name.ToLowerInvariant();
            int score = 0;
            // The company's own server first: published under its domain or GitHub organisation, or at its own address.
            if (s.IsFrom(q)) score += 200;
            if (title == q || title == q + " mcp" || title == "mcp " + q) score += 100;
            if (name.Contains("." + q + ".") || name.StartsWith(q + ".") || name.Contains("/" + q) || name.Contains("." + q + "/") || name.StartsWith("com." + q)) score += 60;
            if (title.Contains(q)) score += 20;
            if (name.Contains(q)) score += 10;
            if (name.StartsWith("ai.smithery/")) score -= 15;
            if (s.Remotes.Count > 0) score += 5;
            return score;
        }
        return servers.Select((s, i) => (s, i)).OrderByDescending(e => Score(e.s)).ThenBy(e => e.i).Select(e => e.s).ToList();
    }

    private static string? Str(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool Bool(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private static RegistryInput Input(JsonObject node, string? nameOverride = null) => new(
        nameOverride ?? Str(node, "name") ?? Str(node, "valueHint") ?? "value",
        Str(node, "description"),
        Bool(node, "isRequired"),
        Bool(node, "isSecret"),
        Str(node, "value"),
        Str(node, "default"),
        Str(node, "format"));

    private static List<RegistryInput> Inputs(JsonNode? node) =>
        (node as JsonArray)?.OfType<JsonObject>().Select(n => Input(n)).ToList() ?? new List<RegistryInput>();

    private static List<RegistryInput> Variables(JsonNode? node) =>
        (node as JsonObject)?.Where(p => p.Value is JsonObject).Select(p => Input((JsonObject)p.Value!, p.Key)).ToList() ?? new List<RegistryInput>();

    private static List<RegistryArgument> Arguments(JsonNode? node) =>
        (node as JsonArray)?.OfType<JsonObject>().Select(a => new RegistryArgument(Str(a, "type") ?? "positional", Str(a, "name"), Input(a))).ToList()
        ?? new List<RegistryArgument>();
}
