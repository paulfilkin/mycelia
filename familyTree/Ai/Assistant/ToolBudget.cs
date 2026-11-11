using System.Text.RegularExpressions;
using Mycelia.Ai.Mcp;

namespace Mycelia.Ai.Assistant;

/// <summary>
/// Keeps the tools sent to the AI service within what it accepts.  OpenAI's chat completions take at most 128 tools
/// ("Invalid 'tools': array too long. Expected an array with maximum length 128"); with several MCP servers connected
/// there can be more.  Then every built-in tool is sent, with find_tools, and the remaining places go to the MCP tools
/// already used or found in this conversation and then to those whose names and descriptions best match the request.
/// find_tools searches all of them, so none is out of reach; what it finds is sent from the next step.  Ported from
/// multifariousStudio's Core/Assistant/ToolBudget.cs.
/// </summary>
public static class ToolBudget
{
    public const string FindToolsName = "find_tools";

    /// <summary>OpenAI's limit, also used for OpenAI-compatible services (Gemini's is the same).</summary>
    public const int OpenAiLimit = 128;

    /// <summary>The most tools the service accepts in one request; null when there is no known limit (Anthropic).</summary>
    public static int? LimitFor(ModelProvider provider) => provider == ModelProvider.Anthropic ? null : OpenAiLimit;

    /// <summary>The limit a service states when it refuses too many tools ("... maximum length 128 ..."), or null.</summary>
    public static int? LimitFromError(string message)
    {
        if (!message.Contains("tools", StringComparison.OrdinalIgnoreCase))
            return null;
        var match = Regex.Match(message, @"maximum (?:length|number(?: of tools)?|of)\D{0,20}(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int limit) && limit > 1 ? limit : null;
    }

    /// <summary>
    /// Lower-case words of three letters or more, split at anything not a letter or digit (so list_records gives list,
    /// record); a word written in camel case counts whole and in its parts (WikiTree gives wikitree, wiki, tree).
    /// </summary>
    public static HashSet<string> Words(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        void Add(string raw)
        {
            string word = raw.ToLowerInvariant();
            if (word.Length >= 3 && !Ignored.Contains(word))
                words.Add(Stem(word));
        }
        foreach (Match match in Regex.Matches(text, "[\\p{L}\\p{Nd}]+"))
        {
            Add(match.Value);
            var parts = Regex.Split(match.Value, "(?<=[a-z])(?=[A-Z])");
            if (parts.Length > 1)
                foreach (string part in parts)
                    Add(part);
        }
        return words;
    }

    /// <summary>Plural and verb endings dropped so records matches record and searching matches search.</summary>
    private static string Stem(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing"))
            return word[..^3];
        if (word.Length > 4 && word.EndsWith("ies"))
            return word[..^3] + "y";
        if (word.Length > 4 && word.EndsWith("es") && !word.EndsWith("ses"))
            return word[..^2];
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss") && !word.EndsWith("us") && !word.EndsWith("is"))
            return word[..^1];
        return word;
    }

    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "this", "that", "from", "into", "are", "was", "you", "your", "can", "please",
        "show", "tell", "what", "which", "who", "how", "all", "any", "some", "about", "give", "get", "use", "using"
    };

    /// <summary>How well a tool matches the words: 3 for each in its name, 1 for each only in its title or description.</summary>
    public static double Score(AssistantTool tool, HashSet<string> words)
    {
        if (words.Count == 0)
            return 0;
        var name = Words(tool.OriginalName.Length > 0 ? tool.OriginalName : tool.Name);
        var text = Words(tool.Title + " " + tool.Description);
        var source = Words(tool.Source);
        double score = 0;
        foreach (string word in words)
        {
            if (name.Contains(word))
                score += 3;
            else if (text.Contains(word))
                score += 1;
            if (source.Contains(word))
                score += 0.5;
        }
        return score;
    }

    /// <summary>
    /// The tools to send: all of them when they fit; otherwise every built-in tool, find_tools, then MCP tools in this
    /// order: those used or found in the conversation (latest first), the best matches for the request, the rest in the
    /// order their servers list them.
    /// </summary>
    public static List<AssistantTool> Choose(IReadOnlyList<AssistantTool> all, int limit, IReadOnlyList<string> recent, string request, AssistantTool findTools)
    {
        if (all.Count <= limit)
            return all.ToList();
        var chosen = all.Where(t => t.IsBuiltIn).Take(Math.Max(0, limit - 1)).ToList();
        chosen.Add(findTools);
        int places = limit - chosen.Count;
        if (places <= 0)
            return chosen;

        var words = Words(request);
        var external = all.Where(t => !t.IsBuiltIn).Select((tool, index) => (tool, index)).ToList();
        int Rank(string name)
        {
            int at = -1;
            for (int i = recent.Count - 1; i >= 0; i--)
                if (recent[i] == name) { at = recent.Count - 1 - i; break; }
            return at < 0 ? int.MaxValue : at;
        }
        chosen.AddRange(external
            .OrderBy(e => Rank(e.tool.Name))
            .ThenByDescending(e => Score(e.tool, words))
            .ThenBy(e => e.index)
            .Take(places)
            .Select(e => e.tool));
        return chosen;
    }

    /// <summary>The MCP tools matching a search, best first (those with no matching word are left out).</summary>
    public static List<AssistantTool> Search(IReadOnlyList<AssistantTool> all, string query, int max)
    {
        var words = Words(query);
        return all.Where(t => !t.IsBuiltIn && t.Name != FindToolsName)
            .Select((tool, index) => (tool, index, score: Score(tool, words)))
            .Where(e => e.score > 0)
            .OrderByDescending(e => e.score).ThenBy(e => e.index)
            .Take(max)
            .Select(e => e.tool)
            .ToList();
    }

    /// <summary>find_tools: searches the tools not sent for lack of room; what it finds is sent from the next step.</summary>
    public static AssistantTool FindTools(Func<IReadOnlyList<AssistantTool>> all, Func<IReadOnlyCollection<string>> sent, Action<IEnumerable<string>> found) => new()
    {
        Name = FindToolsName,
        Title = "Find tools",
        Description = "Finds more of the MCP servers' tools by what they do. Not every tool fits in one request, so only some are offered at a time: " +
                      "when you need one that isn't listed (another of a server's tools, a different task), call this with a few words describing it " +
                      "(for example 'search records' or 'archive catalogue'). The tools it finds are offered from your next step.",
        Schema = Schema.Object(
            ("query", Schema.String("Words describing the tool, or a server's name."), true),
            ("limit", Schema.Integer("Most tools to return (default 15, at most 40)."), false)),
        Policy = ToolPolicy.Allow,
        Run = (args, _) =>
        {
            string query = args.Str("query") ?? string.Empty;
            var tools = all();
            var matches = Search(tools, query, Math.Clamp(args.Int("limit", 15), 1, 40));
            if (matches.Count == 0)
            {
                var servers = tools.Where(t => !t.IsBuiltIn).GroupBy(t => t.Source).Select(g => $"{g.Key} ({g.Count()} tools)");
                return Task.FromResult(new ToolOutcome($"No tools match \"{query}\". The servers are: {string.Join(", ", servers)}. Try other words, or a server's name.", false, "No matches"));
            }
            found(matches.Select(t => t.Name));
            var offered = sent();
            var lines = matches.Select(t => $"- {t.Name}{(offered.Contains(t.Name) ? " (already offered)" : string.Empty)}: {Shorten(t.Description, 200)}");
            return Task.FromResult(new ToolOutcome(
                $"{matches.Count} tools match \"{query}\"; those not already offered are offered from your next step:\n" + string.Join("\n", lines),
                false, $"Found {matches.Count} tools"));
        },
        Describe = args => $"Find tools matching \"{args.Str("query")}\"."
    };

    private static string Shorten(string text, int length)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= length ? text : text[..length] + "…";
    }

    /// <summary>A note for the system prompt when not every tool is sent.</summary>
    public static string PromptNote(int sent, int total, int limit) =>
        $"- Only {sent} of the {total} tools are offered at a time (the AI service accepts at most {limit}). If you need a tool that isn't listed, " +
        $"call {FindToolsName} with words describing it; the tools it finds are offered from your next step. Don't say a server can't do something before searching.";
}
