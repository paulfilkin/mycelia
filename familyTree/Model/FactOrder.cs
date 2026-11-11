namespace Mycelia.Model;

/// <summary>
/// The order facts are listed in, as the Person view's fact table orders them
/// (Design considerations, Person view): by date with undated facts last, and facts on the same date
/// (or both undated) by a fixed order of types.
/// </summary>
public static class FactOrder
{
    /// <summary>
    /// Types placed by this list, in this order; Death and the types after it come after every other type.
    /// Other types rank by their type number.
    /// </summary>
    private static readonly int[] Ranked = [1, 48, 2, 7, 12, 16, 17, 27, 4, 6, 81, 22, 3, 31, 20, 13];

    /// <summary>Rank from which Death, Funeral, Cremation and Burial are numbered.</summary>
    public const int LateRank = 9999;

    private static readonly Dictionary<int, int> Rank = BuildRank();

    /// <summary>Fact types listed under "Personal information" rather than "Individual facts".</summary>
    public static readonly IReadOnlySet<int> PersonalInformationTypes = new HashSet<int> { 1, 48, 47, 46, 85, 53, 92 };

    private static Dictionary<int, int> BuildRank()
    {
        var rank = new Dictionary<int, int>();
        var n = 0;
        foreach (var id in Ranked)
        {
            if (id == FactTypes.Death) n = LateRank;
            rank[id] = n++;
        }
        return rank;
    }

    public static int TypeRank(int typeId) => Rank.TryGetValue(typeId, out var r) ? r : typeId;

    /// <summary>Compares two facts: by date (undated last), then by type rank.</summary>
    public static int Compare(Fact a, Fact b)
    {
        var byDate = CompareDates(a.ParsedDate.SortKey, b.ParsedDate.SortKey);
        return byDate != 0 ? byDate : TypeRank(a.TypeId).CompareTo(TypeRank(b.TypeId));
    }

    private static int CompareDates(long? a, long? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => a.Value.CompareTo(b.Value),
    };

    public static IEnumerable<Fact> Sorted(IEnumerable<Fact> facts)
    {
        var list = facts.ToList();
        // A stable sort, so facts that compare equal keep their stored order.
        return list.Select((f, i) => (f, i)).OrderBy(x => x.f, Comparer<Fact>.Create(Compare)).ThenBy(x => x.i).Select(x => x.f);
    }

    /// <summary>A person's couples in the order of their preferred marriage date, undated last.</summary>
    public static IEnumerable<Family> ByMarriage(IEnumerable<Family> families) =>
        families.Select((f, i) => (f, i))
            .OrderBy(x => x.f.Marriage?.ParsedDate.SortKey, Comparer<long?>.Create(CompareDates))
            .ThenBy(x => x.i)
            .Select(x => x.f);
}
