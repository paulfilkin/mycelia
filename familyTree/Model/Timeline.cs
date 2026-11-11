namespace Mycelia.Model;

public enum TimelineKind { Personal, Family, Historical }

/// <summary>One line on a person's timeline.</summary>
public sealed record TimelineEntry(GenDate Date, string Title, string Detail, TimelineKind Kind, int? Age, Person? Person);

/// <summary>
/// A person's life in date order: their own facts, the births, marriages and deaths of close
/// relatives during it, and historical events of the time.  This app's own design (TDD, Timeline).
/// </summary>
public static class Timeline
{
    /// <summary>How long a life is assumed to last when no death is recorded, for choosing events.</summary>
    public const int AssumedLifespanYears = 100;

    public static List<TimelineEntry> Build(TreeDocument doc, Person p, bool family, bool historical)
    {
        var entries = new List<TimelineEntry>();
        var birth = p.Birth?.ParsedDate;
        var birthYear = birth?.IsValid == true ? birth.Year : null;
        var deathYear = p.Death?.ParsedDate is { IsValid: true } d ? d.Year : null;
        var endYear = deathYear ?? (birthYear + AssumedLifespanYears);

        int? AgeAt(GenDate date)
        {
            if (birth is not { IsValid: true } || !date.IsValid || date.Start.DayNumber < birth.Start.DayNumber) return null;
            return Age.YearsBetween(birth.Start, date.Start);
        }
        bool InLife(GenDate date) =>
            date.IsValid && date.Year is int y && birthYear is int b && y >= b && (endYear is null || y <= endYear);
        string Where(Fact f) => string.Join(", ", new[] { doc.PlaceName(f.PlaceId), f.Description }.Where(s => s.Length > 0));

        foreach (var f in p.Facts.Where(f => !f.ParsedDate.IsEmpty))
            entries.Add(new TimelineEntry(f.ParsedDate, f.Type.Name, Where(f), TimelineKind.Personal, AgeAt(f.ParsedDate), null));
        foreach (var fam in doc.SpouseFamilies(p))
        {
            var spouse = doc.GetPerson(fam.OtherPartner(p.Id));
            foreach (var f in fam.Facts.Where(f => !f.ParsedDate.IsEmpty))
            {
                var detail = string.Join(", ", new[] { spouse is null ? "" : "to " + spouse.DisplayName, Where(f) }.Where(s => s.Length > 0));
                entries.Add(new TimelineEntry(f.ParsedDate, f.Type.Name, detail, TimelineKind.Personal, AgeAt(f.ParsedDate), spouse));
            }
        }

        if (family)
        {
            void Relative(Person r, string role)
            {
                foreach (var (fact, verb) in new[] { (r.Birth, "Birth"), (r.Death, "Death") })
                {
                    if (fact is null || !InLife(fact.ParsedDate)) continue;
                    entries.Add(new TimelineEntry(fact.ParsedDate, $"{verb} of {role}", r.DisplayName, TimelineKind.Family, AgeAt(fact.ParsedDate), r));
                }
            }
            foreach (var parent in doc.Parents(p)) Relative(parent, parent.Sex switch { Sex.Male => "father", Sex.Female => "mother", _ => "parent" });
            foreach (var spouse in doc.Spouses(p)) Relative(spouse, spouse.Sex switch { Sex.Male => "husband", Sex.Female => "wife", _ => "spouse" });
            foreach (var child in doc.Children(p)) Relative(child, child.Sex switch { Sex.Male => "son", Sex.Female => "daughter", _ => "child" });
            foreach (var sib in doc.Siblings(p)) Relative(sib, sib.Sex switch { Sex.Male => "brother", Sex.Female => "sister", _ => "sibling" });
        }

        if (historical)
        {
            foreach (var e in doc.Data.HistoricalEvents)
            {
                var start = GenDate.Parse(e.Date);
                var end = GenDate.Parse(e.EndDate);
                // Shown when any part of the event falls within the life.
                var overlaps = start.IsValid && birthYear is int b && endYear is int last
                               && start.Year <= last && (end.IsValid ? end.Year : start.Year) >= b;
                if (!overlaps) continue;
                var when = end.IsValid ? $"until {end.Display}" : "";
                var detail = string.Join(".  ", new[] { e.Region, when, e.Description }.Where(s => s.Length > 0));
                entries.Add(new TimelineEntry(start, e.Title, detail, TimelineKind.Historical, AgeAt(start), null));
            }
        }

        return entries
            .Select((e, i) => (e, i))
            .OrderBy(x => x.e.Date.SortKey is null ? 1 : 0).ThenBy(x => x.e.Date.SortKey ?? 0).ThenBy(x => x.i)
            .Select(x => x.e).ToList();
    }

    /// <summary>
    /// A starter list of events for timelines, written for this app (dates and short descriptions of
    /// well-known public events, mostly for Britain and Ireland).  Added with "Add the standard list".
    /// </summary>
    public static IReadOnlyList<HistoricalEvent> StandardEvents() =>
    [
        Ev("1801", "", "First national census", "United Kingdom", "Records", "Head counts only; few names survive."),
        Ev("1 Jul 1837", "", "Civil registration begins", "England and Wales", "Records", "Births, marriages and deaths registered by the state."),
        Ev("6 Jun 1841", "", "First census to name everyone", "United Kingdom", "Records", "Ages over 15 usually rounded down to five years."),
        Ev("1845", "1852", "Great Famine", "Ireland", "Social", "Potato blight; mass emigration followed."),
        Ev("1851", "", "Great Exhibition", "United Kingdom", "Culture", "Held in Hyde Park, London."),
        Ev("1 Jan 1855", "", "Civil registration begins", "Scotland", "Records", ""),
        Ev("1861", "1865", "American Civil War", "United States", "War", ""),
        Ev("1 Jan 1864", "", "Civil registration of births and deaths begins", "Ireland", "Records", "Non-Catholic marriages from 1845."),
        Ev("1870", "", "Elementary Education Act", "England and Wales", "Social", "Schools for all children aged 5 to 12."),
        Ev("28 Jul 1914", "11 Nov 1918", "First World War", "World", "War", ""),
        Ev("1918", "", "Many women gain the vote", "United Kingdom", "Politics", "Representation of the People Act."),
        Ev("1918", "1920", "Influenza pandemic", "World", "Health", ""),
        Ev("1929", "1939", "Great Depression", "World", "Economy", ""),
        Ev("1 Sep 1939", "2 Sep 1945", "Second World War", "World", "War", ""),
        Ev("29 Sep 1939", "", "National Register taken", "England and Wales", "Records", "Used for identity cards and rationing."),
        Ev("5 Jul 1948", "", "National Health Service founded", "United Kingdom", "Health", ""),
        Ev("20 Jul 1969", "", "First people walk on the Moon", "World", "Science", ""),
    ];

    private static HistoricalEvent Ev(string date, string end, string title, string region, string category, string description) =>
        new() { Date = date, EndDate = end, Title = title, Region = region, Category = category, Description = description };
}
