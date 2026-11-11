using System.Text;
using System.Text.RegularExpressions;

namespace Mycelia.Model;

/// <summary>Whole-tree tidy-up tools.  Run each inside <see cref="TreeDocument.Change"/> as one undo step.</summary>
public static class TreeTools
{
    // ---------------------------------------------------------------- sort children

    /// <summary>
    /// Puts every family's children in birth order, undated children after the dated ones and
    /// otherwise keeping their order.  Returns how many families changed.
    /// </summary>
    public static int SortAllChildrenByBirth(TreeDocument doc)
    {
        var changed = 0;
        foreach (var fam in doc.Data.Families)
        {
            var sorted = fam.Children
                .Select((c, i) => (c, i, key: doc.GetPerson(c.PersonId)?.Birth?.ParsedDate.SortKey))
                .OrderBy(x => x.key is null ? 1 : 0).ThenBy(x => x.key ?? 0).ThenBy(x => x.i)
                .Select(x => x.c).ToList();
            if (sorted.SequenceEqual(fam.Children)) continue;
            fam.Children = sorted;
            changed++;
        }
        return changed;
    }

    // ---------------------------------------------------------------- convert names

    /// <summary>
    /// Converts every name to mixed case ("First Middle Surname"), or with the surname in capitals
    /// ("First Middle SURNAME") (Design considerations, Convert names).
    /// It converts the main name only; suffixes are left alone, and a name the
    /// test passes over (<see cref="NeedsMixedCase"/>) is not touched.  Returns how many
    /// people changed.
    /// </summary>
    public static int ConvertNames(TreeDocument doc, bool surnameInCapitals)
    {
        var changed = 0;
        foreach (var p in doc.Data.People)
        {
            // The test reads the name as "Given /Surname/".
            if (!NeedsMixedCase(p.Surname.Length > 0 ? $"{p.Given} /{p.Surname}/" : p.Given)) continue;
            var given = string.Join(' ', p.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(GivenWord));
            var surname = surnameInCapitals ? p.Surname.ToUpperInvariant() : MixedSurname(p.Surname);
            if (given == p.Given && surname == p.Surname) continue;
            p.Given = given;
            p.Surname = surname;
            changed++;
        }
        return changed;
    }

    private static readonly Regex Initials = new("^([a-zA-Z]\\.){2,}$", RegexOptions.Compiled);
    private const string OpeningBraces = "[{<(\"'?";

    /// <summary>
    /// The test of whether a name needs converting (Design considerations, Convert names): some word
    /// of three or more characters whose first is not a capital, or whose second or third is not lower
    /// case, with extra checks on a word starting with "/".  Any surname of two or more letters written
    /// "/Surname/" passes the second test, so in practice only names without a surname can be skipped.
    /// </summary>
    public static bool NeedsMixedCase(string name)
    {
        foreach (var w in name.Split(' '))
        {
            if (w.Length <= 2) continue;
            if (w[0] != char.ToUpperInvariant(w[0])
                || (w[0] == '/' && w[1] != char.ToUpperInvariant(w[1]))
                || w[1] != char.ToLowerInvariant(w[1])
                || w[2] != char.ToLowerInvariant(w[2])
                || (w[0] == '/' && w[2] != char.ToUpperInvariant(w[2])))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A given name word: single letters in capitals, each part of a hyphenated
    /// word on its own, "of" and "or" in lower case, others in mixed case.
    /// </summary>
    private static string GivenWord(string w)
    {
        if (w.Length == 1) return w.ToUpperInvariant();
        if (w.EndsWith('.')) return MixedWord(w);
        if (w.Contains('-')) return string.Join('-', w.Split('-').Select(part => part.Length == 0 ? part : MixedWord(part)));
        if (w.Equals("of", StringComparison.OrdinalIgnoreCase) || w.Equals("or", StringComparison.OrdinalIgnoreCase)) return w.ToLowerInvariant();
        return MixedWord(w);
    }

    /// <summary>A surname in mixed case, each part of a hyphenated surname on its own.</summary>
    private static string MixedSurname(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Join('-', word.Split('-').Select(part => part.Length == 0 ? part : MixedWord(part)))));

    /// <summary>
    /// One word in mixed case: Mc and Mac names keep the next letter in capitals
    /// ("McDonald", longer than five and six letters), O' names likewise ("O'Brien"), dotted initials
    /// ("J.R.") stay in capitals, and a leading bracket or quote is kept.
    /// </summary>
    public static string MixedWord(string name)
    {
        var lead = "";
        if (name.Length > 1 && OpeningBraces.Contains(name[0]))
        {
            lead = name[..1];
            name = name[1..];
        }
        if (name.Length > 5 && name.StartsWith("mc", StringComparison.OrdinalIgnoreCase))
            return lead + "Mc" + char.ToUpperInvariant(name[2]) + name[3..].ToLowerInvariant();
        if (name.Length > 6 && name.StartsWith("mac", StringComparison.OrdinalIgnoreCase))
            return lead + "Mac" + char.ToUpperInvariant(name[3]) + name[4..].ToLowerInvariant();
        if (name.Length > 5 && name.StartsWith("o'", StringComparison.OrdinalIgnoreCase))
            return lead + "O'" + char.ToUpperInvariant(name[2]) + name[3..].ToLowerInvariant();
        if (name.Length > 1)
            return lead + (Initials.IsMatch(name) ? name.ToUpperInvariant() : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant());
        return lead + name.ToUpperInvariant();
    }

    // ---------------------------------------------------------------- find and replace

    /// <summary>One place a search matched: what it belongs to, which field, and how to read and write it.</summary>
    public sealed class TextHit(string owner, string field, Func<string> get, Action<string> set, Person? person)
    {
        public string Owner { get; } = owner;
        public string Field { get; } = field;
        public Person? Person { get; } = person;
        public string Text => get();
        public void Write(string value) => set(value);
        public bool Selected { get; set; } = true;
    }

    /// <summary>
    /// Every text field holding <paramref name="query"/>: names, fact descriptions and notes, person and
    /// couple notes, place names, source titles, authors, publishers and comments, citation details,
    /// media captions and descriptions.
    /// </summary>
    public static List<TextHit> Find(TreeDocument doc, string query, bool matchCase, bool wholeWord)
    {
        var hits = new List<TextHit>();
        if (query.Length == 0) return hits;
        var regex = Pattern(query, matchCase, wholeWord);
        void Check(string owner, string field, Func<string> get, Action<string> set, Person? person = null)
        {
            if (regex.IsMatch(get())) hits.Add(new TextHit(owner, field, get, set, person));
        }

        foreach (var p in doc.Data.People)
        {
            var who = p.DisplayName;
            Check(who, "Given names", () => p.Given, v => p.Given = v, p);
            Check(who, "Surname", () => p.Surname, v => p.Surname = v, p);
            Check(who, "Suffix", () => p.Suffix, v => p.Suffix = v, p);
            Check(who, "Notes", () => p.Notes, v => p.Notes = v, p);
            foreach (var f in p.Facts)
            {
                Check(who, $"{f.Type.Name}: description", () => f.Description, v => f.Description = v, p);
                Check(who, $"{f.Type.Name}: note", () => f.Note, v => f.Note = v, p);
                foreach (var c in f.Citations)
                {
                    Check(who, $"{f.Type.Name}: citation detail", () => c.Detail, v => c.Detail = v, p);
                    Check(who, $"{f.Type.Name}: citation text", () => c.Text, v => c.Text = v, p);
                }
            }
        }
        foreach (var fam in doc.Data.Families)
        {
            var who = doc.CoupleName(fam);
            var person = doc.GetPerson(fam.Partner1Id) ?? doc.GetPerson(fam.Partner2Id);
            Check(who, "Notes", () => fam.Notes, v => fam.Notes = v, person);
            foreach (var f in fam.Facts)
            {
                Check(who, $"{f.Type.Name}: description", () => f.Description, v => f.Description = v, person);
                Check(who, $"{f.Type.Name}: note", () => f.Note, v => f.Note = v, person);
                foreach (var c in f.Citations)
                {
                    Check(who, $"{f.Type.Name}: citation detail", () => c.Detail, v => c.Detail = v, person);
                    Check(who, $"{f.Type.Name}: citation text", () => c.Text, v => c.Text = v, person);
                }
            }
        }
        foreach (var pl in doc.Data.Places) Check("Place", "Name", () => pl.Name, v => pl.Name = v);
        foreach (var s in doc.Data.Sources)
        {
            var title = s.ToString();
            Check(title, "Source title", () => s.Title, v => s.Title = v);
            Check(title, "Author", () => s.Author, v => s.Author = v);
            Check(title, "Publisher", () => s.Publisher, v => s.Publisher = v);
            Check(title, "Comments", () => s.Comments, v => s.Comments = v);
        }
        foreach (var m in doc.Data.Media)
        {
            Check(m.Caption.Length > 0 ? m.Caption : "Media", "Caption", () => m.Caption, v => m.Caption = v);
            Check(m.Caption.Length > 0 ? m.Caption : "Media", "Description", () => m.Description, v => m.Description = v);
        }
        return hits;
    }

    /// <summary>Replaces the query in each selected hit; returns how many fields changed.</summary>
    public static int Replace(IEnumerable<TextHit> hits, string query, string replacement, bool matchCase, bool wholeWord)
    {
        var regex = Pattern(query, matchCase, wholeWord);
        var count = 0;
        foreach (var h in hits.Where(h => h.Selected))
        {
            var before = h.Text;
            var after = regex.Replace(before, replacement.Replace("$", "$$"));
            if (after == before) continue;
            h.Write(after);
            count++;
        }
        return count;
    }

    private static Regex Pattern(string query, bool matchCase, bool wholeWord)
    {
        var body = Regex.Escape(query);
        if (wholeWord) body = $@"(?<!\w){body}(?!\w)";
        return new Regex(body, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ---------------------------------------------------------------- branches

    /// <summary>
    /// The people in a branch: the root, then its ancestors and/or descendants, and with
    /// <paramref name="withSpouses"/> the spouses of everyone in it.
    /// </summary>
    public static HashSet<string> Branch(TreeDocument doc, Person root, bool ancestors, bool descendants, bool withSpouses)
    {
        var ids = new HashSet<string> { root.Id };
        if (ancestors)
        {
            var stack = new Stack<Person>([root]);
            while (stack.Count > 0)
                foreach (var parent in doc.Parents(stack.Pop()))
                    if (ids.Add(parent.Id)) stack.Push(parent);
        }
        if (descendants)
        {
            var stack = new Stack<Person>([root]);
            while (stack.Count > 0)
                foreach (var child in doc.Children(stack.Pop()))
                    if (ids.Add(child.Id)) stack.Push(child);
        }
        if (withSpouses)
        {
            foreach (var id in ids.ToList())
                if (doc.GetPerson(id) is { } p)
                    foreach (var s in doc.Spouses(p)) ids.Add(s.Id);
        }
        return ids;
    }

    /// <summary>A short summary for a set of people ("42 people, born 1798-1946").</summary>
    public static string Describe(TreeDocument doc, IReadOnlyCollection<string> ids)
    {
        var years = ids.Select(doc.GetPerson).Select(p => p?.Birth?.ParsedDate.Year).OfType<int>().ToList();
        var sb = new StringBuilder(ids.Count == 1 ? "1 person" : $"{ids.Count} people");
        if (years.Count > 0) sb.Append($", born {years.Min()}-{years.Max()}");
        return sb.ToString();
    }
}
