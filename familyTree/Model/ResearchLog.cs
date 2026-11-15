namespace Mycelia.Model;

/// <summary>
/// The research log on people and couples (TDD 5, Design considerations, Research log): one entry per research
/// question, updated when the question is researched again, so the log reads as one line per question rather than a
/// diary.  These are the rules every change to a log goes through, from the program and from the Assistant.
/// </summary>
public static class ResearchLog
{
    /// <summary>The log of a person or a family; empty, never null.</summary>
    public static IReadOnlyList<ResearchEntry> Of(object owner) => owner switch
    {
        Person p => p.Research ?? [],
        Family f => f.Research ?? [],
        _ => [],
    };

    private static List<ResearchEntry> Ensure(object owner) => owner switch
    {
        Person p => p.Research ??= [],
        Family f => f.Research ??= [],
        _ => throw new ArgumentException("A research log belongs to a person or a family."),
    };

    /// <summary>Sets an empty log back to null, so the file holds nothing for it.</summary>
    public static void Tidy(object owner)
    {
        if (owner is Person { Research.Count: 0 } p) p.Research = null;
        if (owner is Family { Research.Count: 0 } f) f.Research = null;
    }

    /// <summary>The entry for the same question: the same fact when a fact is given, else the same topic, ignoring case and spacing.</summary>
    public static ResearchEntry? Find(object owner, string? factId, string topic) =>
        Of(owner).FirstOrDefault(e => factId is not null && e.FactId == factId)
        ?? Of(owner).FirstOrDefault(e => SameTopic(e.Topic, topic));

    public static bool SameTopic(string a, string b) =>
        string.Equals(Normal(a), Normal(b), StringComparison.CurrentCultureIgnoreCase);

    private static string Normal(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Records research on a question: the existing entry for it is updated (the places searched are added to its list,
    /// and the status, result and next steps replaced when given), or a new entry is made.  Run inside
    /// <see cref="TreeDocument.Change"/>.  Gives the entry and whether it is new.
    /// </summary>
    public static (ResearchEntry Entry, bool Created) Record(object owner, string? factId, string topic, ResearchStatus status,
        IEnumerable<string> searched, string? result, IEnumerable<string>? next, DateTime today)
    {
        var entry = Find(owner, factId, topic);
        var created = entry is null;
        if (entry is null)
        {
            entry = new ResearchEntry { Topic = Normal(topic), FactId = factId };
            Ensure(owner).Add(entry);
        }
        entry.FactId ??= factId;
        entry.Status = status;
        AddDistinct(entry.Searched, searched);
        if (result is not null) entry.Result = result.Trim();
        if (next is not null) entry.Next = next.Select(Normal).Where(s => s.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        else if (status != ResearchStatus.Open) entry.Next.Clear();
        entry.Updated = today.Date;
        return (entry, created);
    }

    private static void AddDistinct(List<string> into, IEnumerable<string> items)
    {
        foreach (var s in items.Select(Normal).Where(s => s.Length > 0))
            if (!into.Contains(s, StringComparer.CurrentCultureIgnoreCase)) into.Add(s);
    }

    /// <summary>
    /// Joins <paramref name="from"/> into <paramref name="into"/> (merging two people or combining two families): an entry
    /// for the same question is combined (places searched added; the result, status and next steps of the later one
    /// kept), the others are added.  Gives null for an empty log.
    /// </summary>
    public static List<ResearchEntry>? Combine(List<ResearchEntry>? into, List<ResearchEntry>? from)
    {
        if (from is null || from.Count == 0) return into is { Count: > 0 } ? into : null;
        into ??= [];
        foreach (var e in from)
        {
            var same = into.FirstOrDefault(x => e.FactId is not null && x.FactId == e.FactId) ?? into.FirstOrDefault(x => SameTopic(x.Topic, e.Topic));
            if (same is null) { into.Add(e); continue; }
            AddDistinct(same.Searched, e.Searched);
            if (e.Updated > same.Updated)
            {
                same.Status = e.Status;
                if (e.Result.Length > 0) same.Result = e.Result;
                same.Next = e.Next;
                same.Updated = e.Updated;
            }
            same.FactId ??= e.FactId;
        }
        return into;
    }
}
