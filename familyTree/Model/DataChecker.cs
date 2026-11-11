using System.Text.RegularExpressions;

namespace Mycelia.Model;

public sealed record DataIssue(Person Person, string Message, string Category);

/// <summary>
/// Finds improbable data.  The checks, limits and date comparisons are in Design considerations,
/// Data error checks.
/// </summary>
public static class DataChecker
{
    public const int ParentMinimumAge = 13;
    public const int MotherMaximumAge = 60;
    public const int FatherMaximumAge = 80;
    public const int QuestionableDeathAge = 120;
    public const int FatherDeathBeforeChildDays = 330;
    public const int DuplicateFactWindowDays = 60;
    public const int SimilarTextMaximumDistance = 20;

    /// <summary>Fact types a person should only have once; two of them are always a possible duplicate.</summary>
    private static readonly HashSet<int> OnceOnlyTypes = [1, 2, 3, 8, 9, 11, 13, 17, 19, 20, 27, 30, 31, 40, 43, 48, 54, 59, 82, 88];

    private static readonly Regex IllegalNameCharacters = new("[!@#$%^&*+={}|;:/~]", RegexOptions.Compiled);

    public static List<DataIssue> Check(TreeDocument doc)
    {
        var issues = new List<DataIssue>();
        foreach (var p in doc.Data.People.OrderBy(p => p.IndexName, StringComparer.CurrentCultureIgnoreCase))
            issues.AddRange(CheckPerson(doc, p).DistinctBy(i => i.Message));
        return issues;
    }

    // ---- date comparison (Design considerations, Comparing two dates) ----

    /// <summary>
    /// Signed difference from <paramref name="a"/> to <paramref name="b"/>, compared only to the precision both dates share:
    /// months only when both have a month, days only when both have a month and a day.  Ranges and text dates give null.
    /// </summary>
    private static (long Days, int Years)? Difference(Fact? a, Fact? b)
    {
        if (a is null || b is null) return null;
        var da = a.ParsedDate;
        var db = b.ParsedDate;
        if (!da.IsValid || !db.IsValid || da.IsRange || db.IsRange) return null;
        if (da.Start.Year is not int y1 || db.Start.Year is not int y2) return null;
        int? m1 = null, m2 = null, d1 = null, d2 = null;
        if (da.Start.Month is not null && db.Start.Month is not null) { m1 = da.Start.Month; m2 = db.Start.Month; }
        if (da.Start.Day is not null && db.Start.Day is not null) { d1 = da.Start.Day; d2 = db.Start.Day; }
        if (m1 is null || d1 is null) { d1 = d2 = null; }
        var j1 = GenDate.ToDayNumber(y1, m1 ?? 1, d1 ?? 1);
        var j2 = GenDate.ToDayNumber(y2, m2 ?? 1, d2 ?? 1);
        int years;
        if (j2 >= j1) years = Age.Between(y1, m1 ?? 1, d1 ?? 1, y2, m2 ?? 1, d2 ?? 1).Years;
        else years = -Age.Between(y2, m2 ?? 1, d2 ?? 1, y1, m1 ?? 1, d1 ?? 1).Years;
        return (j2 - j1, years);
    }

    /// <summary>Fact a comes more than <paramref name="days"/> days before fact b.</summary>
    private static bool Before(Fact? a, Fact? b, int days = 0) => Difference(a, b) is { } d && d.Days > days;

    /// <summary>Fact a comes after fact b.</summary>
    private static bool After(Fact? a, Fact? b) => Difference(a, b) is { } d && d.Days < 0;

    private static bool YearsUnder(Fact? from, Fact? to, int years) => Difference(from, to) is { } d && d.Years < years;
    private static bool YearsOver(Fact? from, Fact? to, int years) => Difference(from, to) is { } d && d.Years > years;

    // ---- checks ----

    public static IEnumerable<DataIssue> CheckPerson(TreeDocument doc, Person p)
    {
        DataIssue Issue(string message, string category = "Dates") => new(p, message, category);

        var birth = p.Birth;
        var death = p.Death;
        var burial = p.Preferred(FactTypes.Burial);
        var father = doc.Father(p);
        var mother = doc.Mother(p);
        var families = doc.SpouseFamilies(p).ToList();
        var preferredFamily = families.FirstOrDefault();
        var marriage = preferredFamily?.Marriage;
        var hasSpouse = preferredFamily is not null && preferredFamily.OtherPartner(p.Id) is not null;
        var children = doc.Children(p).ToList();

        if (birth is not null)
        {
            if (After(birth, death)) yield return Issue("The birth date occurs after the death date.");
            if (YearsUnder(mother?.Birth, birth, ParentMinimumAge)) yield return Issue($"Born when the mother was under {ParentMinimumAge}.", "Parents");
            if (YearsOver(mother?.Birth, birth, MotherMaximumAge)) yield return Issue($"Born when the mother was over {MotherMaximumAge}.", "Parents");
            if (After(birth, mother?.Death)) yield return Issue("Born after the mother's death.", "Parents");
            if (YearsUnder(father?.Birth, birth, ParentMinimumAge)) yield return Issue($"Born when the father was under {ParentMinimumAge}.", "Parents");
            if (YearsOver(father?.Birth, birth, FatherMaximumAge)) yield return Issue($"Born when the father was over {FatherMaximumAge}.", "Parents");
            if (Before(father?.Death, birth, FatherDeathBeforeChildDays)) yield return Issue($"Born more than {FatherDeathBeforeChildDays} days after the father's death.", "Parents");

            var others = p.Facts.Where(f => f.Preferred && f != birth)
                .Concat(families.SelectMany(f => f.Facts.Where(x => x.Preferred)));
            foreach (var f in others)
                if (After(birth, f)) yield return Issue($"The birth is after the {f.Type.Name.ToLowerInvariant()} ({f.ParsedDate.Display}).");

            if (hasSpouse && YearsUnder(birth, marriage, ParentMinimumAge)) yield return Issue($"Married before the age of {ParentMinimumAge}.");
            if (YearsOver(birth, death, QuestionableDeathAge)) yield return Issue($"The age at death is greater than {QuestionableDeathAge}.");

            foreach (var child in children)
            {
                var cb = child.Birth;
                if (YearsOver(cb, birth, 0)) yield return Issue($"The birth is after the birth of {child.DisplayName}.", "Parents");
                else if (YearsOver(cb, birth, -ParentMinimumAge)) yield return Issue($"Was less than {ParentMinimumAge} years old when {child.DisplayName} was born.", "Parents");
                if (YearsUnder(birth, cb, -ParentMinimumAge)) yield return Issue($"Born more than {ParentMinimumAge} years after {child.DisplayName}.", "Parents");
            }
        }

        if (death is not null)
        {
            if (Before(death, marriage)) yield return Issue("The marriage occurred after the death date.");
            if (After(death, burial)) yield return Issue("Burial date before death date.");
            var allowance = p.Sex == Sex.Male ? FatherDeathBeforeChildDays : 0;
            foreach (var child in children)
                if (Before(death, child.Birth, allowance)) yield return Issue($"The death is before the birth of {child.DisplayName}.", "Parents");
        }

        if (burial is not null && Before(burial, death)) yield return Issue("Burial date before death date.");

        foreach (var fam in families)
        {
            var spouse = doc.GetPerson(fam.OtherPartner(p.Id));
            var m = fam.Marriage;
            var divorce = fam.Facts.FirstOrDefault(f => f.TypeId == FactTypes.Divorce && f.Preferred) ?? fam.Facts.FirstOrDefault(f => f.TypeId == FactTypes.Divorce);
            if (m is not null)
            {
                if (After(m, death)) yield return Issue("The marriage occurred after the death date.");
                if (spouse is not null && After(m, divorce)) yield return Issue("The divorce occurred before the marriage date.");
                if (After(m, spouse?.Death)) yield return Issue($"Married after the death of {spouse!.DisplayName}.");
                if (YearsUnder(birth, m, ParentMinimumAge)) yield return Issue($"Married before the age of {ParentMinimumAge}.");
                if (YearsUnder(spouse?.Birth, m, ParentMinimumAge)) yield return Issue($"{spouse!.DisplayName} was under {ParentMinimumAge} at the marriage.");
            }
            if (spouse is not null && divorce is not null && Before(divorce, m)) yield return Issue("The divorce occurred before the marriage date.");
        }

        foreach (var f in p.Facts.Concat(families.SelectMany(x => x.Facts)))
        {
            if (Before(f, birth)) yield return Issue($"{f.Type.Name} ({f.ParsedDate.Display}) occurs before the birth date.");
            if (!f.ParsedDate.IsEmpty && !f.ParsedDate.IsValid) yield return Issue($"{f.Type.Name} date \"{f.Date}\" is not a valid date.");
            if (f.ParsedDate.IsEmpty && f.PlaceId is null && f.Description.Length == 0) yield return Issue($"{f.Type.Name} has no date, place or description.", "Missing");
        }

        foreach (var typeId in DuplicateFactTypes(doc, p.Facts))
            yield return Issue($"Possible duplicate {FactTypes.Get(typeId).Name.ToLowerInvariant()} facts.", "Facts");

        // Name checks
        var fullName = $"{p.Given} {p.Surname} {p.Suffix}".Trim();
        if (fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith('-') || w.EndsWith('-')))
            yield return Issue("The name has a misplaced dash.", "Names");
        if (IllegalNameCharacters.IsMatch(fullName))
            yield return Issue("The name contains characters names do not normally have.", "Names");
        var preferredSpouse = doc.GetPerson(preferredFamily?.OtherPartner(p.Id));
        if (preferredSpouse is not null && p.Surname.Length > 0 && p.Given.Length > 0 && p.Surname != "?" && p.Given != "?" && preferredSpouse.Given != "?"
            && string.Equals(p.Surname, preferredSpouse.Surname, StringComparison.InvariantCultureIgnoreCase))
            yield return Issue("Has the same surname as the spouse.", "Names");

        // Relationship checks (Design considerations, Relationship checks): this program's own.
        foreach (var spouseId in families.Select(f => f.OtherPartner(p.Id)).OfType<string>().GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key))
            if (doc.GetPerson(spouseId) is { } twice)
                yield return Issue($"Recorded twice as a couple with {twice.DisplayName}.", "Relationships");
        var naturalTo = doc.ParentFamilies(p)
            .SelectMany(f => f.Children.Where(c => c.PersonId == p.Id).SelectMany(c => new[] { (Id: f.Partner1Id, Nature: c.Nature1), (Id: f.Partner2Id, Nature: c.Nature2) }))
            .Where(x => x.Id is not null && x.Nature == ChildNature.Natural)
            .GroupBy(x => x.Id!).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var parentId in naturalTo)
            if (doc.GetPerson(parentId) is { } parent)
                yield return Issue($"Natural child of {parent.DisplayName} in two families.", "Relationships");
        if (p.IsUnnamed)
            yield return Issue("Has no name, or only \"?\" or \"Unknown\".", "Relationships");

        // Missing details
        if (birth is null || birth.ParsedDate.IsEmpty) yield return Issue("Birth date is missing.", "Missing");
        if (preferredFamily is not null && hasSpouse && preferredFamily.Type == CoupleType.Spouse && (marriage is null || marriage.ParsedDate.IsEmpty))
            yield return Issue("Marriage date is missing.", "Missing");
        if (preferredFamily is not null && ChildrenOutOfOrder(doc, preferredFamily)) yield return Issue("Children are not in birth order.", "Missing");
        if (p.Sex == Sex.Unknown) yield return Issue("Sex is unknown.", "Missing");
        if (families.Count == 0 && !doc.ParentFamilies(p).Any())
            yield return Issue("Person is not attached to anyone in the tree.", "Missing");
    }

    private static bool ChildrenOutOfOrder(TreeDocument doc, Family family)
    {
        Fact? previous = null;
        foreach (var child in doc.Children(family))
        {
            var b = child.Birth;
            if (b is null || !b.ParsedDate.IsValid) continue;
            if (previous is null) { previous = b; continue; }
            if (Difference(previous, b) is { } d)
            {
                if (d.Days < 0) return true;
                previous = b;
            }
            else if (previous.ParsedDate.Year is null)
            {
                previous = b;
            }
        }
        return false;
    }

    /// <summary>The duplicate fact rule (Design considerations, Duplicate facts).</summary>
    public static IEnumerable<int> DuplicateFactTypes(TreeDocument doc, IReadOnlyList<Fact> facts)
    {
        foreach (var group in facts.GroupBy(f => f.TypeId).Where(g => g.Count() > 1))
        {
            if (OnceOnlyTypes.Contains(group.Key)) { yield return group.Key; continue; }
            var list = group.ToList();
            var found = false;
            for (var i = 0; i < list.Count && !found; i++)
            for (var j = 0; j < list.Count && !found; j++)
            {
                if (i == j) continue;
                int compared = 0, alike = 0;
                if (Difference(list[i], list[j]) is { } d)
                {
                    compared++;
                    if (Math.Abs(d.Days) < DuplicateFactWindowDays) alike++;
                }
                if (list[i].PlaceId is not null && list[j].PlaceId is not null)
                {
                    compared++;
                    if (SimilarText(doc.PlaceName(list[i].PlaceId), doc.PlaceName(list[j].PlaceId), ignoreCase: false)) alike++;
                }
                if (list[i].Description.Length > 0 && list[j].Description.Length > 0)
                {
                    compared++;
                    if (SimilarText(list[i].Description, list[j].Description, ignoreCase: true)) alike++;
                }
                found = compared > 0 && compared == alike;
            }
            if (found) yield return group.Key;
        }
    }

    /// <summary>Close enough: edit distance under half the longer text (at most 20), or one contains the other.</summary>
    private static bool SimilarText(string a, string b, bool ignoreCase)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (ignoreCase) { a = a.ToLowerInvariant(); b = b.ToLowerInvariant(); }
        var limit = Math.Min(Math.Max(a.Length, b.Length) / 2, SimilarTextMaximumDistance);
        return EditDistance(a, b) < limit || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static int EditDistance(string s, string t)
    {
        var prev = new int[t.Length + 1];
        var cur = new int[t.Length + 1];
        for (var j = 0; j <= t.Length; j++) prev[j] = j;
        for (var i = 1; i <= s.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[t.Length];
    }
}

/// <summary>American Soundex codes for surname searches.</summary>
public static class Soundex
{
    public static string Code(string? name)
    {
        var letters = (name ?? "").ToUpperInvariant().Where(char.IsAsciiLetter).ToArray();
        if (letters.Length == 0) return "";
        static char Digit(char c) => c switch
        {
            'B' or 'F' or 'P' or 'V' => '1',
            'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
            'D' or 'T' => '3',
            'L' => '4',
            'M' or 'N' => '5',
            'R' => '6',
            'H' or 'W' => '-',
            _ => '0',
        };
        var code = new System.Text.StringBuilder().Append(letters[0]);
        var last = Digit(letters[0]);
        for (var i = 1; i < letters.Length && code.Length < 4; i++)
        {
            var d = Digit(letters[i]);
            if (d == '-') continue; // H and W do not separate equal codes
            if (d != '0' && d != last) code.Append(d);
            last = d;
        }
        return code.ToString().PadRight(4, '0');
    }
}
