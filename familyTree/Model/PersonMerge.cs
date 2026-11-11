namespace Mycelia.Model;

/// <summary>
/// Merging two records of the same person, and finding likely duplicates.  These rules are this
/// app's own design (TDD, Merging and duplicates).
/// </summary>
public static class PersonMerge
{
    /// <summary>
    /// Folds <paramref name="merge"/> into <paramref name="keep"/> and removes it.  Run inside
    /// <see cref="TreeDocument.Change"/> so the whole merge is one undo step.
    /// </summary>
    public static void Merge(TreeDocument doc, Person keep, Person merge)
    {
        if (keep == merge) return;

        // Names: fill gaps, and keep a different name as an alternative.
        var otherName = merge.DisplayName;
        if (keep.Given.Length == 0) keep.Given = merge.Given;
        if (keep.Surname.Length == 0) keep.Surname = merge.Surname;
        if (keep.Suffix.Length == 0) keep.Suffix = merge.Suffix;
        if (merge.Given.Length + merge.Surname.Length > 0
            && !string.Equals(otherName, keep.DisplayName, StringComparison.CurrentCultureIgnoreCase)
            && !keep.Facts.Any(f => f.TypeId == FactTypes.AlsoKnownAs && string.Equals(f.Description, otherName, StringComparison.CurrentCultureIgnoreCase)))
        {
            keep.Facts.Add(new Fact { TypeId = FactTypes.AlsoKnownAs, Description = otherName, Preferred = !keep.Facts.Any(f => f.TypeId == FactTypes.AlsoKnownAs) });
        }
        if (keep.Sex == Sex.Unknown) keep.Sex = merge.Sex;
        keep.Private |= merge.Private;
        keep.Notes = JoinText(keep.Notes, merge.Notes);
        keep.PhotoMediaId ??= merge.PhotoMediaId;

        MergeFacts(keep.Facts, merge.Facts);

        foreach (var m in doc.Data.Media)
        {
            if (!m.PersonIds.Remove(merge.Id)) continue;
            if (!m.PersonIds.Contains(keep.Id)) m.PersonIds.Add(keep.Id);
        }

        // Relationships: every link to the merged record now points at the kept one.
        foreach (var fam in doc.Data.Families)
        {
            if (fam.Partner1Id == merge.Id) fam.Partner1Id = keep.Id;
            if (fam.Partner2Id == merge.Id) fam.Partner2Id = keep.Id;
            if (fam.Partner1Id == keep.Id && fam.Partner2Id == keep.Id) fam.Partner2Id = null;
            foreach (var link in fam.Children.Where(c => c.PersonId == merge.Id)) link.PersonId = keep.Id;
            var seen = new HashSet<string>();
            fam.Children.RemoveAll(c => !seen.Add(c.PersonId) || c.PersonId == fam.Partner1Id || c.PersonId == fam.Partner2Id);
        }
        CombineSameCouples(doc);

        if (doc.Data.HomePersonId == merge.Id) doc.Data.HomePersonId = keep.Id;
        for (var i = 0; i < doc.Data.History.Count; i++)
            if (doc.Data.History[i] == merge.Id) doc.Data.History[i] = keep.Id;

        doc.Data.People.Remove(merge);
        doc.Data.Families.RemoveAll(f => f.Partner1Id is null && f.Partner2Id is null && f.Children.Count == 0 && f.Facts.Count == 0);
    }

    /// <summary>Adds facts not already there; a fact matching one already there only adds its sources and missing details.</summary>
    private static void MergeFacts(List<Fact> into, List<Fact> from)
    {
        foreach (var f in from)
        {
            var same = into.FirstOrDefault(x => x.TypeId == f.TypeId
                && SameDate(x.ParsedDate, f.ParsedDate)
                && (x.PlaceId == f.PlaceId || x.PlaceId is null || f.PlaceId is null)
                && (x.Description.Length == 0 || f.Description.Length == 0 || string.Equals(x.Description, f.Description, StringComparison.CurrentCultureIgnoreCase)));
            if (same is not null)
            {
                // Keep the fuller of two agreeing dates ("21 Jul 1856" over "1856").
                if (Precision(f.ParsedDate) > Precision(same.ParsedDate)) same.Date = f.Date;
                same.PlaceId ??= f.PlaceId;
                if (same.Description.Length == 0) same.Description = f.Description;
                same.Note = JoinText(same.Note, f.Note);
                foreach (var c in f.Citations)
                    if (!same.Citations.Any(x => x.SourceId == c.SourceId && x.Detail == c.Detail))
                        same.Citations.Add(c);
                continue;
            }
            // A second fact of a type already there becomes an alternate, as when typed in.
            f.Preferred = !into.Any(x => x.TypeId == f.TypeId);
            into.Add(f);
        }
    }

    /// <summary>
    /// Two dates describe the same day when they are equal, or when one only leaves out parts the other
    /// has ("1856" and "21 Jul 1856"; "Mar 1856" and "3 Mar 1856") with the same modifier.  Ranges and text
    /// dates must match exactly.
    /// </summary>
    private static bool SameDate(GenDate a, GenDate b)
    {
        if (string.Equals(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (!a.IsValid || !b.IsValid || a.IsRange || b.IsRange || a.Modifier != b.Modifier) return false;
        var (x, y) = (a.Start, b.Start);
        if (x.Year != y.Year) return false;
        if (x.Month is not null && y.Month is not null && x.Month != y.Month) return false;
        if (x.Day is not null && y.Day is not null && x.Day != y.Day) return false;
        return (x.Month is not null || x.Day is null) && (y.Month is not null || y.Day is null);
    }

    private static int Precision(GenDate d) =>
        !d.IsValid ? 0 : (d.Start.Year is null ? 0 : 1) + (d.Start.Month is null ? 0 : 1) + (d.Start.Day is null ? 0 : 1);

    /// <summary>After a merge two families can hold the same couple; they become one.</summary>
    private static void CombineSameCouples(TreeDocument doc)
    {
        var families = doc.Data.Families;
        for (var i = 0; i < families.Count; i++)
        for (var j = families.Count - 1; j > i; j--)
        {
            var a = families[i];
            var b = families[j];
            if (a.Partner1Id is null || a.Partner2Id is null) continue;
            var samePair = (a.Partner1Id == b.Partner1Id && a.Partner2Id == b.Partner2Id) || (a.Partner1Id == b.Partner2Id && a.Partner2Id == b.Partner1Id);
            if (!samePair) continue;
            MergeFacts(a.Facts, b.Facts);
            foreach (var c in b.Children)
                if (!a.Children.Any(x => x.PersonId == c.PersonId)) a.Children.Add(c);
            a.Notes = JoinText(a.Notes, b.Notes);
            families.RemoveAt(j);
        }
    }

    private static string JoinText(string a, string b)
    {
        if (b.Length == 0 || string.Equals(a, b, StringComparison.Ordinal)) return a;
        return a.Length == 0 ? b : a + Environment.NewLine + Environment.NewLine + b;
    }

    // ---------------------------------------------------------------- duplicates

    public sealed record Candidate(Person A, Person B, string Reason, int Score);

    /// <summary>Most pairs listed; a tree with more than this needs tidying a part at a time.</summary>
    public const int MaxCandidates = 500;

    /// <summary>Birth years within this many years count as the same person.</summary>
    public const int BirthYearTolerance = 2;

    /// <summary>
    /// Likely duplicates: surnames that sound alike (Soundex), given names that agree (the same first
    /// given name, or one is an initial of the other), sexes that do not conflict, birth years within
    /// <see cref="BirthYearTolerance"/> where both are known, and not parent, child or spouse of each other.
    /// </summary>
    public static List<Candidate> FindDuplicates(TreeDocument doc)
    {
        var result = new List<Candidate>();
        var bySound = doc.Data.People
            .Where(p => p.Surname.Length > 0 || p.Given.Length > 0)
            .GroupBy(p => Soundex.Code(p.Surname));
        foreach (var group in bySound)
        {
            var people = group.ToList();
            for (var i = 0; i < people.Count; i++)
            for (var j = i + 1; j < people.Count; j++)
            {
                var c = Compare(doc, people[i], people[j]);
                if (c is not null) result.Add(c);
                if (result.Count >= MaxCandidates * 4) break;
            }
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.A.IndexName, StringComparer.CurrentCultureIgnoreCase).Take(MaxCandidates).ToList();
    }

    private static Candidate? Compare(TreeDocument doc, Person a, Person b)
    {
        if (a.Sex != Sex.Unknown && b.Sex != Sex.Unknown && a.Sex != b.Sex) return null;

        var ga = FirstGiven(a);
        var gb = FirstGiven(b);
        int score;
        if (ga.Length == 0 || gb.Length == 0) return null;
        if (string.Equals(ga, gb, StringComparison.CurrentCultureIgnoreCase)) score = 50;
        else if (SameGivenName(ga, gb)) score = 40;
        else if ((ga.Length == 1 || gb.Length == 1) && char.ToUpperInvariant(ga[0]) == char.ToUpperInvariant(gb[0])) score = 30;
        else return null;
        if (string.Equals(a.Surname, b.Surname, StringComparison.CurrentCultureIgnoreCase)) score += 20;
        else score += 10;

        var reasons = new List<string> { score >= 70 ? "same name" : "similar name" };
        var ya = a.Birth?.ParsedDate.Year;
        var yb = b.Birth?.ParsedDate.Year;
        if (ya is int y1 && yb is int y2)
        {
            if (Math.Abs(y1 - y2) > BirthYearTolerance) return null;
            score += y1 == y2 ? 25 : 15;
            reasons.Add(y1 == y2 ? $"both born {y1}" : $"born {y1} and {y2}");
        }
        else if (!string.Equals(a.Given, b.Given, StringComparison.CurrentCultureIgnoreCase))
        {
            // With no birth years to compare, ask for the whole given names to agree.
            return null;
        }
        else
        {
            reasons.Add("no birth year to compare");
        }

        var placeA = doc.PlaceName(a.Birth?.PlaceId);
        if (placeA.Length > 0 && string.Equals(placeA, doc.PlaceName(b.Birth?.PlaceId), StringComparison.CurrentCultureIgnoreCase))
        {
            score += 10;
            reasons.Add("same birth place");
        }

        // Close relatives with the same name are usually different people (a father and son).
        if (doc.Parents(a).Contains(b) || doc.Parents(b).Contains(a) || doc.Spouses(a).Contains(b)) return null;

        return new Candidate(a, b, string.Join(", ", reasons), score);
    }

    /// <summary>Abbreviations of given names common in old records.</summary>
    private static readonly Dictionary<string, string> GivenAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Wm"] = "William", ["Thos"] = "Thomas", ["Jas"] = "James", ["Chas"] = "Charles", ["Geo"] = "George",
        ["Jno"] = "John", ["Jos"] = "Joseph", ["Robt"] = "Robert", ["Richd"] = "Richard", ["Edw"] = "Edward",
        ["Edwd"] = "Edward", ["Saml"] = "Samuel", ["Benj"] = "Benjamin", ["Danl"] = "Daniel", ["Fredk"] = "Frederick",
        ["Hy"] = "Henry", ["Eliz"] = "Elizabeth", ["Margt"] = "Margaret", ["Mgt"] = "Margaret", ["Cath"] = "Catherine",
    };

    /// <summary>The same first given name written differently: a known abbreviation, or one name starting the other (at least three letters).</summary>
    private static bool SameGivenName(string a, string b)
    {
        string Full(string s) => GivenAbbreviations.TryGetValue(s, out var full) ? full : s;
        var fa = Full(a);
        var fb = Full(b);
        if (string.Equals(fa, fb, StringComparison.CurrentCultureIgnoreCase)) return true;
        var (shorter, longer) = fa.Length <= fb.Length ? (fa, fb) : (fb, fa);
        return shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string FirstGiven(Person p) =>
        p.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.') ?? "";
}
