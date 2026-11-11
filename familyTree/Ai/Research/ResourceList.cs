using System.Text.RegularExpressions;

namespace Mycelia.Ai.Research;

/// <summary>One row of the resources list: a site, how it is used, where, its cost and its API and MCP status.</summary>
public sealed record Resource(string Section, string Name, string Website, string Use, string Focus, string Cost, string Api, string Mcp, string Notes)
{
    public bool Worldwide => Regex.IsMatch(Focus, @"^(Worldwide|Europe-wide|Atlantic world)\b", RegexOptions.IgnoreCase);
    public bool Free => Cost.StartsWith("Free", StringComparison.OrdinalIgnoreCase);
    /// <summary>A documented API that is open or free to ask for (not "None public", "Not confirmed", "Paid API"...).</summary>
    public bool HasApi => Regex.IsMatch(Api, @"^(Yes|Data available through)|web service|REST API", RegexOptions.IgnoreCase);

    /// <summary>An MCP server of any kind: the owner's own, or a community one.</summary>
    public bool HasMcp => Regex.IsMatch(Mcp, @"^(Yes|Official|Community)", RegexOptions.IgnoreCase);
}

/// <summary>
/// The genealogy resources list (Docs/Resources/genealogy-resources.md), built into the program and read as it is
/// (TDD 25.3).  Updating the list means replacing that file and building again.
/// </summary>
public static class ResourceList
{
    private const string ResourceName = "Mycelia.genealogy-resources.md";

    private static (string Compiled, IReadOnlyList<Resource> Rows, IReadOnlyList<string> Legend, IReadOnlyList<string> Practical)? _list;

    public static string Compiled => Load().Compiled;
    public static IReadOnlyList<Resource> All => Load().Rows;

    /// <summary>The list's notes on how to read it (cost words, †, MCP kinds).</summary>
    public static IReadOnlyList<string> Legend => Load().Legend;

    /// <summary>The list's practical notes (free first, indexes and images, automation, privacy, community servers).</summary>
    public static IReadOnlyList<string> Practical => Load().Practical;

    private static (string Compiled, IReadOnlyList<Resource> Rows, IReadOnlyList<string> Legend, IReadOnlyList<string> Practical) Load()
    {
        if (_list is { } list)
            return list;
        using var stream = typeof(ResourceList).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException("The resources list isn't built into this program.");
        using var reader = new StreamReader(stream);
        var loaded = Parse(reader.ReadToEnd());
        _list = loaded;
        return loaded;
    }

    /// <summary>Reads the Markdown: the compile date, every table row under a numbered section, and two lists of notes.</summary>
    public static (string Compiled, IReadOnlyList<Resource> Rows, IReadOnlyList<string> Legend, IReadOnlyList<string> Practical) Parse(string markdown)
    {
        string compiled = Regex.Match(markdown, @"Compiled ([^.\r\n]+)\.") is { Success: true } m ? m.Groups[1].Value.Trim() : "";
        var rows = new List<Resource>();
        var legend = new List<string>();
        var practical = new List<string>();
        string section = "";
        string heading = "";
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                heading = line[3..].Trim();
                section = Regex.Match(heading, @"^\d+\.\s+(.+)$") is { Success: true } s ? s.Groups[1].Value : "";
                continue;
            }
            if (heading.StartsWith("How to read", StringComparison.OrdinalIgnoreCase) && line.TrimStart().StartsWith("- "))
                legend.Add(Plain(line.Trim()[2..]));
            else if (heading.StartsWith("Practical notes", StringComparison.OrdinalIgnoreCase) && line.StartsWith("- "))
                practical.Add(Plain(line[2..]));
            if (section.Length == 0 || !line.StartsWith('|'))
                continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 8 || cells[0] == "Resource" || cells[0].StartsWith("---"))
                continue;
            rows.Add(new Resource(section, cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7]));
        }
        return (compiled, rows, legend, practical);
    }

    private static string Plain(string text) => text.Replace("**", "").Replace("*", "").Trim();

    // ---- Finding ----

    private static readonly Dictionary<string, string[]> PlaceAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uk"] = ["United Kingdom", "England", "Wales", "Scotland", "Ireland", "UK"],
        ["britain"] = ["United Kingdom", "England", "Wales", "Scotland", "UK"],
        ["us"] = ["United States", "Americas"],
        ["usa"] = ["United States", "Americas"],
        ["america"] = ["United States", "Americas", "North America"],
        ["holland"] = ["Netherlands"],
        ["prussia"] = ["Germany", "German Empire", "Prussian"],
        ["nz"] = ["New Zealand"],
    };

    private static readonly Dictionary<string, string[]> TopicAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["church"] = ["church", "parish"],
        ["parish"] = ["parish", "church"],
        ["baptism"] = ["parish", "church", "vital"],
        ["bmd"] = ["birth", "marriage", "death", "civil registration", "vital"],
        ["civil"] = ["civil registration"],
        ["will"] = ["will", "probate", "estate"],
        ["probate"] = ["will", "probate", "estate"],
        ["military"] = ["military", "service", "war"],
        ["army"] = ["military", "service", "war"],
        ["grave"] = ["grave", "cemetery", "burial", "memorial"],
        ["burial"] = ["grave", "cemetery", "burial", "memorial"],
        ["emigration"] = ["emigration", "immigration", "passenger"],
        ["immigration"] = ["emigration", "immigration", "passenger", "naturalisation"],
        ["map"] = ["map", "gazetteer", "place"],
        ["place"] = ["map", "gazetteer", "place"],
    };

    /// <summary>
    /// The rows matching a place (country or region: the focus, section or name), a kind of record (words in the
    /// name, use, notes or section, any of them), and the cost, API and MCP filters.  Worldwide rows are kept for a
    /// place unless left out.
    /// </summary>
    public static List<Resource> Find(string? place, string? recordType, bool freeOnly, bool withApi, bool withMcp, bool includeWorldwide = true)
    {
        var placeTerms = Terms(place, PlaceAliases);
        var topicTerms = Terms(recordType, TopicAliases);
        return All.Where(r =>
                (placeTerms.Count == 0 || placeTerms.Any(t => Contains(r.Focus + " " + r.Section + " " + r.Name, t)) || (includeWorldwide && r.Worldwide))
                && (topicTerms.Count == 0 || topicTerms.Any(t => Contains(r.Name + " " + r.Use + " " + r.Notes + " " + r.Section, t)))
                && (!freeOnly || r.Free)
                && (!withApi || r.HasApi)
                && (!withMcp || r.HasMcp))
            .ToList();
    }

    private static List<string> Terms(string? text, Dictionary<string, string[]> aliases)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var terms = new List<string>();
        // The whole phrase, then each word of three letters or more (a plural's s dropped), with its aliases.
        terms.Add(text.Trim());
        foreach (var word in Regex.Split(text, @"[^\p{L}\p{N}]+").Where(w => w.Length >= 2))
        {
            var w = word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;
            if (aliases.TryGetValue(w, out var more) || aliases.TryGetValue(word, out more))
                terms.AddRange(more);
            if (w.Length >= 3)
                terms.Add(w);
        }
        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Contains(string text, string term) =>
        Regex.IsMatch(text, @"(?<![\p{L}])" + Regex.Escape(term), RegexOptions.IgnoreCase);
}
