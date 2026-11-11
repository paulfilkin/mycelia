using System.Text.RegularExpressions;

namespace Mycelia.Ui;

/// <summary>One section of a help page: the text under a ## or ### heading, up to the next one.</summary>
public sealed record HelpSection(string Page, string Heading, string Anchor, string Text)
{
    /// <summary>The section on GitHub, where F1 opens the pages.</summary>
    public string Address => Help.BaseAddress + Page + (Anchor.Length > 0 ? "#" + Anchor : "");
}

/// <summary>
/// The help pages (Docs/Help), built into the program so the Assistant's read_help can answer "how do I" and "where is"
/// questions from them rather than describing screens it has never seen (TDD 22.3, 28).  Updating the help means
/// building again.
/// </summary>
public static class HelpPages
{
    private const string Prefix = "Mycelia.Help.";

    private static IReadOnlyList<HelpSection>? _sections;

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "this", "that", "from", "into", "are", "was", "you", "your", "can", "how", "what",
        "where", "which", "when", "does", "find", "see", "use", "mycelia", "there", "its", "have", "has", "not", "show",
    };

    /// <summary>Every section of every page, in page order.</summary>
    public static IReadOnlyList<HelpSection> All => _sections ??= Load();

    /// <summary>The page names, such as "sources.md".</summary>
    public static IEnumerable<string> PageNames => All.Select(s => s.Page).Distinct();

    /// <summary>The sections that best match the words of a question, best first; headings count more than the text.</summary>
    public static IReadOnlyList<HelpSection> Search(string question, string? page, int limit)
    {
        var words = Regex.Matches(question.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => Stem(m.Value))
            .Where(w => w.Length > 2 && !Ignored.Contains(w)).Distinct().ToList();
        if (words.Count == 0) return [];
        return All.Where(s => page is null || s.Page.Equals(page, StringComparison.OrdinalIgnoreCase))
            .Select(s => (Section: s, Score: Score(s, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Section)
            .ToList();
    }

    private static int Score(HelpSection s, List<string> words)
    {
        var heading = s.Heading.ToLowerInvariant();
        var text = s.Text.ToLowerInvariant();
        int score = 0, matched = 0;
        foreach (var w in words)
        {
            // A stem matches the start of a word: "sav" finds save, saved and saving.
            var start = @"\b" + Regex.Escape(w);
            int hits = Regex.Matches(text, start).Count;
            bool inHeading = Regex.IsMatch(heading, start);
            if (hits > 0 || inHeading) matched++;
            score += Math.Min(hits, 4) + (inHeading ? 6 : 0);
        }
        // A section holding more of the question's words beats one that repeats a single word.
        return score * matched;
    }

    /// <summary>A word cut to its stem, matched against the start of words: citations, citing and cited all give "cit".</summary>
    private static string Stem(string word)
    {
        foreach (var ending in new[] { "ations", "ation", "ings", "ing", "ies", "ied", "es", "ed", "s", "e", "y" })
            if (word.EndsWith(ending) && word.Length - ending.Length >= 3 && !(ending == "s" && word.EndsWith("ss")))
                return word[..^ending.Length];
        return word;
    }

    private static List<HelpSection> Load()
    {
        var assembly = typeof(HelpPages).Assembly;
        var sections = new List<HelpSection>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            sections.AddRange(Parse(name[Prefix.Length..], reader.ReadToEnd()));
        }
        return sections;
    }

    /// <summary>Splits a page at its ## and ### headings; the text above the first is the page's own section.</summary>
    public static IEnumerable<HelpSection> Parse(string page, string markdown)
    {
        string heading = "", anchor = "";
        var text = new List<string>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var h = Regex.Match(line, @"^(#{1,3})\s+(.+)$");
            if (h.Success && (h.Groups[1].Length > 1 || heading.Length == 0))
            {
                if (Section(page, heading, anchor, text) is { } done) yield return done;
                heading = h.Groups[2].Value.Trim();
                anchor = h.Groups[1].Length == 1 ? "" : Slug(heading);
                text.Clear();
                continue;
            }
            // Pictures say nothing to a model that can't see them.
            if (line.TrimStart().StartsWith("![")) continue;
            text.Add(Absolute(page, line));
        }
        if (Section(page, heading, anchor, text) is { } last) yield return last;
    }

    private static HelpSection? Section(string page, string heading, string anchor, List<string> lines)
    {
        var text = Regex.Replace(string.Join("\n", lines).Trim(), @"\n{3,}", "\n\n");
        return heading.Length == 0 && text.Length == 0 ? null : new HelpSection(page, heading, anchor, text);
    }

    /// <summary>GitHub's anchor for a heading: small letters, punctuation dropped, spaces as hyphens.</summary>
    public static string Slug(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{N}\s_-]", "").Trim().Replace(' ', '-');

    /// <summary>Links to other help pages made into full addresses, so a link given on still opens.</summary>
    private static string Absolute(string page, string line) =>
        Regex.Replace(line, @"\]\((?!https?:|mailto:)([^)\s]*)\)", m =>
        {
            var target = m.Groups[1].Value;
            return "](" + Help.BaseAddress + (target.StartsWith('#') ? page + target : target) + ")";
        });
}
