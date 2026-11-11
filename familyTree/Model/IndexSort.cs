namespace Mycelia.Model;

/// <summary>The index's sort orders (Design considerations, Index sorting).</summary>
public enum IndexSortOrder { FamilyName, GivenName, BirthDate, MarriageDate, DeathDate }

/// <summary>
/// Orders people for the index: by name with the birth date as tie-breaker,
/// or by a date with the reversed name as tie-breaker.  People without the date go last.
/// </summary>
public static class IndexSort
{
    /// <summary>Most entries kept in the history (HistoryListManager.MAXCOUNT).</summary>
    public const int HistoryLimit = 400;

    public static List<Person> Sort(TreeDocument doc, IEnumerable<Person> people, IndexSortOrder order)
    {
        var text = StringComparer.CurrentCulture;
        IComparer<long?> dates = Comparer<long?>.Create(CompareDates);

        long? Birth(Person p) => p.Birth?.ParsedDate.SortKey;
        long? Death(Person p) => p.Death?.ParsedDate.SortKey;
        long? Marriage(Person p) => doc.SpouseFamilies(p).FirstOrDefault()?.Marriage?.ParsedDate.SortKey;

        return order switch
        {
            IndexSortOrder.GivenName => people.OrderBy(p => p.DisplayName, text).ThenBy(Birth, dates).ToList(),
            IndexSortOrder.BirthDate => people.OrderBy(Birth, dates).ThenBy(p => p.IndexName, text).ToList(),
            IndexSortOrder.MarriageDate => people.OrderBy(Marriage, dates).ThenBy(p => p.IndexName, text).ToList(),
            IndexSortOrder.DeathDate => people.OrderBy(Death, dates).ThenBy(p => p.IndexName, text).ToList(),
            _ => people.OrderBy(p => p.IndexName, text).ThenBy(Birth, dates).ToList(),
        };
    }

    /// <summary>Dates in order; a missing date sorts after every real one.</summary>
    private static int CompareDates(long? a, long? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => a.Value.CompareTo(b.Value),
    };
}
