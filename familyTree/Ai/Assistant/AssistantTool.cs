using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mycelia.Ai.Mcp;

namespace Mycelia.Ai.Assistant;

/// <summary>What a tool gives back to the model, and a one-line summary for the person.</summary>
public sealed record ToolOutcome(string Content, bool IsError = false, string? Summary = null);

/// <summary>
/// A tool the assistant can use: one of Mycelia's own, or one from an MCP server.  Name is what the model sees
/// (letters, digits, '_' and '-', at most 64, as both Anthropic and OpenAI require).  Ported from multifariousStudio.
/// </summary>
public sealed class AssistantTool
{
    public const int MaxNameLength = 64;
    public const string BuiltInSource = "Mycelia";

    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required JsonObject Schema { get; init; }

    /// <summary>Who provides it: Mycelia or the MCP server's name.</summary>
    public string Source { get; init; } = BuiltInSource;

    /// <summary>The MCP server's id; null for built-in tools.</summary>
    public string? ServerId { get; init; }

    /// <summary>The tool's own name on its server.</summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary>The tool changes the tree, files or the app (asked about by default).</summary>
    public bool Changes { get; init; }

    public ToolPolicy Policy { get; set; }

    /// <summary>The policy before the person changes it; Ask for tools that change something, else Allow, unless set.</summary>
    public ToolPolicy? DefaultPolicy { get; init; }

    public ToolPolicy Default => DefaultPolicy ?? (Changes ? ToolPolicy.Ask : ToolPolicy.Allow);

    /// <summary>
    /// A call that runs without asking even when the policy is Ask (fetch_url for the listed archive APIs, TDD 25.4);
    /// never asked when null.
    /// </summary>
    public Func<JsonObject, bool>? AllowWithoutAsking { get; init; }

    /// <summary>
    /// What "Always allow" on the approval card means for a call, when it isn't the whole tool: it remembers what it needs
    /// (fetch_url: the site) and returns true, and the tool's policy stays as it was (TDD 22.4).  Null: the whole tool.
    /// </summary>
    public Func<JsonObject, bool>? AllowAlwaysCall { get; init; }

    /// <summary>The approval card's "always" button, and the outcome it leaves.</summary>
    public string AlwaysLabel { get; init; } = "Always allow";
    public string AlwaysOutcome { get; init; } = "Allowed (always for this tool)";

    /// <summary>The tool sends requests to the web (openWorldHint for other programs, TDD 24).</summary>
    public bool ReachesWeb { get; init; }

    /// <summary>The call needs the person's answer first: Ask, unless this call is one that may run without asking.</summary>
    public bool NeedsApproval(JsonObject args) => Policy == ToolPolicy.Ask && !(AllowWithoutAsking?.Invoke(args) ?? false);

    public required Func<JsonObject, CancellationToken, Task<ToolOutcome>> Run { get; init; }

    /// <summary>A plain sentence saying what a call will do, for the approval card.</summary>
    public Func<JsonObject, string>? Describe { get; init; }

    /// <summary>Whether the tool can be offered now; always when null.</summary>
    public Func<bool>? Available { get; init; }

    public bool IsBuiltIn => ServerId == null;

    public ToolSpec ToSpec() => new(Name, Description, Schema);

    /// <summary>"server__tool", cleaned to the allowed characters and shortened with a hash when too long.</summary>
    public static string ModelName(string serverId, string tool)
    {
        string raw = $"{serverId}__{tool}";
        var clean = new string(raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        if (clean.Length <= MaxNameLength)
            return clean;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..8].ToLowerInvariant();
        return clean[..(MaxNameLength - 9)] + "_" + hash;
    }
}

/// <summary>Small helpers for writing JSON Schemas and reading arguments.</summary>
public static class Schema
{
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in properties)
        {
            // A JSON node can belong to one parent only; shared argument schemas are copied.
            props[name] = schema.DeepClone();
            if (isRequired)
                required.Add(name);
        }
        var result = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0)
            result["required"] = required;
        return result;
    }

    public static JsonObject String(string description, params string[] choices)
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = description };
        if (choices.Length > 0)
            schema["enum"] = new JsonArray(choices.Select(c => (JsonNode)c).ToArray());
        return schema;
    }

    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject Array(JsonObject items, string description) =>
        new() { ["type"] = "array", ["items"] = items.DeepClone(), ["description"] = description };

    public static string? Str(this JsonObject args, string name)
    {
        var node = args[name];
        if (node == null)
            return null;
        string text = node is JsonValue value && value.TryGetValue(out string? s) ? s : node.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static int Int(this JsonObject args, string name, int fallback)
    {
        var node = args[name];
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out int number))
                return number;
            if (value.TryGetValue(out double real))
                return (int)real;
            if (value.TryGetValue(out string? text) && int.TryParse(text, out number))
                return number;
        }
        return fallback;
    }

    public static bool Bool(this JsonObject args, string name, bool fallback)
    {
        var node = args[name];
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out bool flag))
                return flag;
            if (value.TryGetValue(out string? text) && bool.TryParse(text, out flag))
                return flag;
        }
        return fallback;
    }

    public static List<string> Strings(this JsonObject args, string name)
    {
        var node = args[name];
        if (node is JsonArray array)
            return array.Select(n => n?.ToString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        if (node != null && node.ToString() is { Length: > 0 } single)
            return single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new List<string>();
    }
}
