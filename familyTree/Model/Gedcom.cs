using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mycelia.Model;

/// <summary>Reads and writes GEDCOM 5.5.1, the common file format for exchanging family trees.</summary>
public static class Gedcom
{
    private sealed class Node
    {
        public int Level;
        public string? XRef;
        public string Tag = "";
        public string Value = "";
        public List<Node> Children = [];

        public Node? Child(string tag) => Children.FirstOrDefault(c => c.Tag == tag);
        public string Text(string tag) => Child(tag)?.FullValue ?? "";

        /// <summary>The value with its CONT and CONC lines joined.</summary>
        public string FullValue
        {
            get
            {
                var sb = new StringBuilder(Value);
                foreach (var c in Children)
                {
                    if (c.Tag == "CONT") sb.Append('\n').Append(c.Value);
                    else if (c.Tag == "CONC") sb.Append(c.Value);
                }
                return sb.ToString();
            }
        }
    }

    private static readonly Regex LineRegex = new(@"^\s*(\d+)\s+(?:(@[^@]+@)\s+)?(\S+)(?:\s(.*))?$", RegexOptions.Compiled);

    // ---------------------------------------------------------------- import

    public static TreeData Import(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Decode(bytes);
        var roots = Parse(text);
        var data = new TreeData { Name = Path.GetFileNameWithoutExtension(path) };
        var doc = new TreeDocument(data);

        var notes = roots.Where(r => r.Tag == "NOTE" && r.XRef is not null).ToDictionary(r => r.XRef!, r => r.FullValue);
        var repoMap = new Dictionary<string, Repository>();
        var sourceMap = new Dictionary<string, Source>();
        var mediaMap = new Dictionary<string, MediaItem>();
        var personMap = new Dictionary<string, Person>();

        foreach (var r in roots.Where(r => r.Tag == "REPO"))
        {
            var repo = new Repository
            {
                Name = r.Text("NAME"),
                Address = r.Text("ADDR"),
                Phone = r.Text("PHON"),
                Email = r.Text("EMAIL"),
            };
            data.Repositories.Add(repo);
            if (r.XRef is not null) repoMap[r.XRef] = repo;
        }

        foreach (var s in roots.Where(r => r.Tag == "SOUR"))
        {
            var src = new Source
            {
                Title = s.Text("TITL") is { Length: > 0 } t ? t : s.Text("ABBR"),
                Author = s.Text("AUTH"),
                Publisher = s.Text("PUBL"),
                Comments = NoteText(s, notes),
                Url = WebAddress(s),
            };
            var repoRef = s.Child("REPO");
            if (repoRef is not null)
            {
                if (repoMap.TryGetValue(repoRef.Value, out var repo)) src.RepositoryId = repo.Id;
                src.CallNumber = repoRef.Text("CALN");
            }
            data.Sources.Add(src);
            if (s.XRef is not null) sourceMap[s.XRef] = src;
        }

        foreach (var o in roots.Where(r => r.Tag == "OBJE" && r.XRef is not null))
        {
            var m = MediaFrom(o);
            data.Media.Add(m);
            mediaMap[o.XRef!] = m;
        }

        foreach (var i in roots.Where(r => r.Tag == "INDI"))
        {
            var p = new Person();
            var nameNode = i.Child("NAME");
            if (nameNode is not null)
            {
                var (given, surname, suffix) = SplitName(nameNode.Value);
                p.Given = nameNode.Text("GIVN") is { Length: > 0 } g ? g : given;
                p.Surname = nameNode.Text("SURN") is { Length: > 0 } s ? s : surname;
                p.Suffix = nameNode.Text("NSFX") is { Length: > 0 } x ? x : suffix;
            }
            foreach (var alias in i.Children.Where(c => c.Tag == "NAME").Skip(1))
            {
                var (g2, s2, x2) = SplitName(alias.Value);
                p.Facts.Add(new Fact { TypeId = FactTypes.AlsoKnownAs, Description = string.Join(' ', new[] { g2, s2, x2 }.Where(v => v.Length > 0)) });
            }
            p.Sex = i.Text("SEX").Trim().ToUpperInvariant() switch { "M" => Sex.Male, "F" => Sex.Female, _ => Sex.Unknown };
            p.Notes = NoteText(i, notes);
            foreach (var c in i.Children)
            {
                if (c.Tag == "EVEN" || c.Tag == "FACT")
                {
                    var typeName = c.Text("TYPE");
                    var known = NamedType(data, typeName, FactScope.Person, c.Tag == "EVEN");
                    if (known is not null) p.Facts.Add(FactFrom(c, known, doc, sourceMap));
                    continue;
                }
                var type = FactTypes.ByTag(c.Tag, FactScope.Person);
                if (type is null || type.Id == FactTypes.AlsoKnownAs && c.Tag == "NAME") continue;
                p.Facts.Add(FactFrom(c, type, doc, sourceMap));
            }
            foreach (var f in p.Facts.GroupBy(f => f.TypeId))
            {
                var first = true;
                foreach (var fact in f) { fact.Preferred = first; first = false; }
            }
            foreach (var o in i.Children.Where(c => c.Tag == "OBJE"))
            {
                var media = o.Value.StartsWith('@') ? mediaMap.GetValueOrDefault(o.Value) : MediaFrom(o);
                if (media is null) continue;
                if (!data.Media.Contains(media)) data.Media.Add(media);
                media.PersonIds.Add(p.Id);
                p.PhotoMediaId ??= media.IsImage ? media.Id : null;
            }
            data.People.Add(p);
            if (i.XRef is not null) personMap[i.XRef] = p;
        }

        foreach (var f in roots.Where(r => r.Tag == "FAM"))
        {
            var husband = personMap.GetValueOrDefault(f.Text("HUSB"));
            var wife = personMap.GetValueOrDefault(f.Text("WIFE"));
            var fam = new Family { Partner1Id = husband?.Id, Partner2Id = wife?.Id, Notes = NoteText(f, notes) };
            foreach (var c in f.Children)
            {
                if (c.Tag == "CHIL")
                {
                    if (!personMap.TryGetValue(c.Value, out var child)) continue;
                    fam.Children.Add(new ChildLink
                    {
                        PersonId = child.Id,
                        Nature1 = NatureFrom(c.Text("_FREL")),
                        Nature2 = NatureFrom(c.Text("_MREL")),
                    });
                    continue;
                }
                var type = c.Tag is "EVEN" or "FACT"
                    ? NamedType(data, c.Text("TYPE"), FactScope.Couple, c.Tag == "EVEN")
                    : FactTypes.ByTag(c.Tag, FactScope.Couple);
                if (type is not null) fam.Facts.Add(FactFrom(c, type, doc, sourceMap));
            }
            if (fam.Facts.Any(x => x.TypeId == FactTypes.Divorce)) fam.Status = CoupleStatus.Divorced;
            data.Families.Add(fam);
        }

        // Child natures written on the child's FAMC link (PEDI) by some programs.
        foreach (var i in roots.Where(r => r.Tag == "INDI" && r.XRef is not null))
        {
            foreach (var famc in i.Children.Where(c => c.Tag == "FAMC" && c.Child("PEDI") is not null))
            {
                var nature = NatureFrom(famc.Text("PEDI"));
                var famNode = roots.FirstOrDefault(r => r.Tag == "FAM" && r.XRef == famc.Value);
                if (famNode is null) continue;
                var idx = roots.Where(r => r.Tag == "FAM").ToList().IndexOf(famNode);
                if (idx < 0 || idx >= data.Families.Count) continue;
                var link = data.Families[idx].Children.FirstOrDefault(l => l.PersonId == personMap[i.XRef!].Id);
                if (link is not null) { link.Nature1 = nature; link.Nature2 = nature; }
            }
        }

        data.HomePersonId = data.People.FirstOrDefault()?.Id;
        return data;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4000));
        var charset = Regex.Match(head, @"1\s+CHAR\s+(\S+)").Groups[1].Value.ToUpperInvariant();
        if (charset is "UTF-8" or "UTF8" or "UNICODE") return Encoding.UTF8.GetString(bytes);
        try
        {
            // Strict UTF-8 first: many files say ANSI but are UTF-8.
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static List<Node> Parse(string text)
    {
        var roots = new List<Node>();
        var stack = new List<Node>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var m = LineRegex.Match(line);
            if (!m.Success) continue;
            var node = new Node
            {
                Level = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                XRef = m.Groups[2].Success ? m.Groups[2].Value : null,
                Tag = m.Groups[3].Value.ToUpperInvariant(),
                Value = m.Groups[4].Success ? m.Groups[4].Value : "",
            };
            while (stack.Count > node.Level) stack.RemoveAt(stack.Count - 1);
            if (node.Level == 0 || stack.Count == 0) roots.Add(node);
            else stack[^1].Children.Add(node);
            stack.Add(node);
        }
        return roots;
    }

    private static (string Given, string Surname, string Suffix) SplitName(string value)
    {
        var m = Regex.Match(value, @"^(.*?)/(.*?)/(.*)$");
        if (!m.Success) return (value.Trim(), "", "");
        return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim());
    }

    private static string NoteText(Node n, Dictionary<string, string> notes)
    {
        var parts = new List<string>();
        foreach (var c in n.Children.Where(c => c.Tag == "NOTE"))
            parts.Add(c.Value.StartsWith('@') ? notes.GetValueOrDefault(c.Value, "") : c.FullValue);
        return string.Join("\n\n", parts.Where(p => p.Length > 0));
    }

    private static Fact FactFrom(Node n, FactType type, TreeDocument doc, Dictionary<string, Source> sources)
    {
        var fact = new Fact
        {
            TypeId = type.Id,
            Date = GenDate.FromGedcom(n.Text("DATE")),
            PlaceId = doc.GetOrCreatePlace(n.Text("PLAC"))?.Id,
            Description = n.Value.Trim() is { Length: > 0 } v && v != "Y" ? v : n.Text("TYPE") is { Length: > 0 } t && n.Tag != "EVEN" ? t : "",
            Note = n.Children.Where(c => c.Tag == "NOTE" && !c.Value.StartsWith('@')).Select(c => c.FullValue).FirstOrDefault() ?? "",
        };
        foreach (var s in n.Children.Where(c => c.Tag == "SOUR"))
        {
            if (sources.TryGetValue(s.Value, out var src))
                fact.Citations.Add(new Citation
                {
                    SourceId = src.Id,
                    Detail = s.Text("PAGE"),
                    // The web address under any of the tags programs use (TDD 25): GEDCOM 7's WWW, or _LINK, _URL.
                    Url = WebAddress(s),
                    Accessed = s.Text("_ACCESSED") is { Length: > 0 } a ? GenDate.FromGedcom(a) : "",
                    Text = s.Child("DATA")?.Text("TEXT") ?? "",
                });
        }
        return fact;
    }

    /// <summary>A web address written as WWW (GEDCOM 7), _LINK or _URL (TDD 25).</summary>
    private static string WebAddress(Node n) =>
        new[] { "WWW", "_LINK", "_URL" }.Select(n.Text).FirstOrDefault(v => v.Length > 0)?.Trim() ?? "";

    private static MediaItem MediaFrom(Node o)
    {
        var file = o.Child("FILE");
        return new MediaItem
        {
            FilePath = file?.Value ?? "",
            Caption = file?.Text("TITL") is { Length: > 0 } t ? t : o.Text("TITL"),
            Date = GenDate.FromGedcom(o.Text("DATE")),
        };
    }

    private static ChildNature NatureFrom(string value) => value.Trim().ToLowerInvariant() switch
    {
        "adopted" => ChildNature.Adopted,
        "step" or "stepchild" => ChildNature.Step,
        "foster" => ChildNature.Foster,
        "related" => ChildNature.Related,
        "guardian" => ChildNature.Guardian,
        "sealing" or "sealed" => ChildNature.Sealed,
        "private" => ChildNature.Private,
        "unknown" => ChildNature.Unknown,
        _ => ChildNature.Natural,
    };

    /// <summary>
    /// An EVEN or FACT record's type by its TYPE name: a built-in type of that name, else the tree's own
    /// type of that name, else a new custom type (EVEN has a date and place, FACT a value only).
    /// </summary>
    private static FactType? NamedType(TreeData data, string typeName, FactScope scope, bool isEvent)
    {
        var name = typeName.Trim();
        if (name.Length == 0) return null;
        var list = scope == FactScope.Person ? FactTypes.PersonTypes : FactTypes.CoupleTypes;
        var known = list.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known;
        var custom = new CustomFactType { Id = TreeImport.NextCustomId(data), Name = name, Scope = scope, HasDate = isEvent, HasPlace = isEvent };
        data.CustomFactTypes.Add(custom);
        FactTypes.SetCustom(data.CustomFactTypes);
        return FactTypes.Get(custom.Id);
    }

    // ---------------------------------------------------------------- export

    /// <summary>What an export holds (File > Export GEDCOM, Person > Export branch).</summary>
    public sealed class ExportOptions
    {
        /// <summary>Only these people (a branch); everyone when null.</summary>
        public HashSet<string>? PersonIds { get; init; }

        /// <summary>Write facts and people marked Private.  Off: private facts are left out and private people are written as living people are when privatised.</summary>
        public bool IncludePrivate { get; init; }

        /// <summary>Write living people (Design considerations, Living people) with the given name "Living", their surname and sex, and nothing else.</summary>
        public bool PrivatiseLiving { get; init; }
    }

    /// <summary>The word that replaces a privatised person's given names.</summary>
    public const string LivingName = "Living";

    public static void Export(TreeDocument doc, string path, ExportOptions? options = null)
    {
        options ??= new ExportOptions { IncludePrivate = true };
        var data = doc.Data;
        var people = data.People.Where(p => options.PersonIds is null || options.PersonIds.Contains(p.Id)).ToList();
        var included = people.Select(p => p.Id).ToHashSet();
        var hidden = people.Where(p => (!options.IncludePrivate && p.Private) || (options.PrivatiseLiving && Living.IsLiving(doc, p)))
            .Select(p => p.Id).ToHashSet();
        var families = data.Families.Where(f =>
            (f.Partner1Id is not null && included.Contains(f.Partner1Id)) || (f.Partner2Id is not null && included.Contains(f.Partner2Id))
            || f.Children.Any(c => included.Contains(c.PersonId))).ToList();
        bool Writes(Fact f) => options.IncludePrivate || !f.Private;
        IEnumerable<Fact> FactsOf(Person p) => hidden.Contains(p.Id) ? [] : p.Facts.Where(Writes);
        // A couple with a hidden partner keeps its links but not its facts or notes.
        bool HidesCouple(Family f) =>
            (f.Partner1Id is not null && hidden.Contains(f.Partner1Id)) || (f.Partner2Id is not null && hidden.Contains(f.Partner2Id));
        IEnumerable<Fact> FamilyFacts(Family f) => HidesCouple(f) ? [] : f.Facts.Where(Writes);
        var cited = people.SelectMany(FactsOf).Concat(families.SelectMany(FamilyFacts)).SelectMany(f => f.Citations).Select(c => c.SourceId).ToHashSet();
        var sources = options.PersonIds is null ? data.Sources : data.Sources.Where(s => cited.Contains(s.Id)).ToList();
        var repoIds = sources.Select(s => s.RepositoryId).OfType<string>().ToHashSet();
        var repositories = options.PersonIds is null ? data.Repositories : data.Repositories.Where(r => repoIds.Contains(r.Id)).ToList();
        var media = data.Media.Where(m => options.PersonIds is null && m.PersonIds.Count == 0 || m.PersonIds.Any(id => included.Contains(id) && !hidden.Contains(id))).ToList();

        var personRef = new Dictionary<string, string>();
        var familyRef = new Dictionary<string, string>();
        var sourceRef = new Dictionary<string, string>();
        var repoRef = new Dictionary<string, string>();
        var mediaRef = new Dictionary<string, string>();
        for (var i = 0; i < people.Count; i++) personRef[people[i].Id] = $"@I{i + 1}@";
        for (var i = 0; i < families.Count; i++) familyRef[families[i].Id] = $"@F{i + 1}@";
        for (var i = 0; i < sources.Count; i++) sourceRef[sources[i].Id] = $"@S{i + 1}@";
        for (var i = 0; i < repositories.Count; i++) repoRef[repositories[i].Id] = $"@R{i + 1}@";
        for (var i = 0; i < media.Count; i++) mediaRef[media[i].Id] = $"@M{i + 1}@";

        var w = new StringBuilder();
        void Line(int level, string tag, string? value = null, string? xref = null)
        {
            var text = value ?? "";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var li = 0; li < lines.Length; li++)
            {
                var chunk = lines[li];
                var lvl = li == 0 ? level : level + 1;
                var tg = li == 0 ? tag : "CONT";
                var first = true;
                do
                {
                    var take = Math.Min(chunk.Length, 200);
                    var piece = chunk[..take];
                    chunk = chunk[take..];
                    w.Append(lvl.ToString(CultureInfo.InvariantCulture));
                    if (li == 0 && first && xref is not null) w.Append(' ').Append(xref);
                    w.Append(' ').Append(first ? tg : "CONC");
                    if (piece.Length > 0) w.Append(' ').Append(piece);
                    w.Append("\r\n");
                    // Continuation pieces sit one level below the line they continue.
                    lvl = level + 1;
                    first = false;
                } while (chunk.Length > 0);
            }
        }

        Line(0, "HEAD");
        Line(1, "SOUR", "MYCELIA");
        Line(2, "NAME", "Mycelia");
        Line(2, "VERS", "1.0");
        Line(1, "DATE", DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant());
        Line(1, "GEDC");
        Line(2, "VERS", "5.5.1");
        Line(2, "FORM", "LINEAGE-LINKED");
        Line(1, "CHAR", "UTF-8");
        Line(1, "SUBM", "@U1@");
        Line(0, "SUBM", null, "@U1@");
        Line(1, "NAME", "Mycelia user");

        void WriteFact(Fact f, int level)
        {
            var type = f.Type;
            var hasDetail = f.Date.Length > 0 || f.PlaceId is not null || f.Citations.Count > 0 || f.Note.Length > 0;
            var value = f.Description.Length > 0 ? f.Description : hasDetail ? null : "Y";
            if (FactTypes.IsCustom(type.Id))
            {
                // The tree's own types: EVEN (or FACT for a value only) with a TYPE line naming them.
                Line(level, type.HasDate ? "EVEN" : "FACT", f.Description.Length > 0 ? f.Description : null);
                Line(level + 1, "TYPE", type.Name);
            }
            else
            {
                Line(level, type.Tag, value);
            }
            if (f.Date.Length > 0) Line(level + 1, "DATE", f.ParsedDate.ToGedcom());
            if (doc.PlaceName(f.PlaceId) is { Length: > 0 } place) Line(level + 1, "PLAC", place);
            if (f.Note.Length > 0) Line(level + 1, "NOTE", f.Note);
            foreach (var c in f.Citations)
            {
                if (!sourceRef.TryGetValue(c.SourceId, out var sr)) continue;
                Line(level + 1, "SOUR", sr);
                if (c.Detail.Length > 0) Line(level + 2, "PAGE", c.Detail);
                // The transcription in the standard DATA.TEXT; the web address as GEDCOM 7's WWW, which most programs
                // read; when it was looked at as this program's own _ACCESSED (TDD 25).
                if (c.Text.Length > 0)
                {
                    Line(level + 2, "DATA");
                    Line(level + 3, "TEXT", c.Text);
                }
                if (c.Url.Length > 0) Line(level + 2, "WWW", c.Url);
                if (c.Accessed.Length > 0) Line(level + 2, "_ACCESSED", GenDate.Parse(c.Accessed) is { IsValid: true } d ? d.ToGedcom() : c.Accessed);
            }
        }

        foreach (var p in people)
        {
            var isHidden = hidden.Contains(p.Id);
            var given = isHidden ? LivingName : p.Given;
            var suffix = isHidden ? "" : p.Suffix;
            Line(0, "INDI", null, personRef[p.Id]);
            Line(1, "NAME", $"{given} /{p.Surname}/{(suffix.Length > 0 ? " " + suffix : "")}".Trim());
            if (given.Length > 0) Line(2, "GIVN", given);
            if (p.Surname.Length > 0) Line(2, "SURN", p.Surname);
            if (suffix.Length > 0) Line(2, "NSFX", suffix);
            Line(1, "SEX", p.Sex switch { Sex.Male => "M", Sex.Female => "F", _ => "U" });
            foreach (var f in FactsOf(p).OrderBy(f => f.ParsedDate.SortKey ?? long.MaxValue))
            {
                if (f.TypeId == FactTypes.AlsoKnownAs)
                {
                    var (g, s, x) = SplitAka(f.Description);
                    Line(1, "NAME", $"{g} /{s}/ {x}".Trim());
                    Line(2, "TYPE", "aka");
                    continue;
                }
                WriteFact(f, 1);
            }
            foreach (var fam in families.Where(f => f.Children.Any(c => c.PersonId == p.Id)))
            {
                Line(1, "FAMC", familyRef[fam.Id]);
                var link = fam.Children.First(c => c.PersonId == p.Id);
                if (link.Nature1 == link.Nature2 && link.Nature1 != ChildNature.Natural)
                    Line(2, "PEDI", link.Nature1 switch { ChildNature.Adopted => "adopted", ChildNature.Foster => "foster", ChildNature.Sealed => "sealing", _ => "birth" });
            }
            foreach (var fam in families.Where(f => f.HasPartner(p.Id)))
                Line(1, "FAMS", familyRef[fam.Id]);
            if (isHidden) continue;
            foreach (var m in media.Where(m => m.PersonIds.Contains(p.Id)))
                Line(1, "OBJE", mediaRef[m.Id]);
            if (p.Notes.Length > 0) Line(1, "NOTE", p.Notes);
        }

        foreach (var fam in families)
        {
            Line(0, "FAM", null, familyRef[fam.Id]);
            if (fam.Partner1Id is not null && personRef.TryGetValue(fam.Partner1Id, out var h)) Line(1, "HUSB", h);
            if (fam.Partner2Id is not null && personRef.TryGetValue(fam.Partner2Id, out var wf)) Line(1, "WIFE", wf);
            foreach (var c in fam.Children)
            {
                if (!personRef.TryGetValue(c.PersonId, out var cr)) continue;
                Line(1, "CHIL", cr);
                if (c.Nature1 != ChildNature.Natural || c.Nature2 != ChildNature.Natural)
                {
                    Line(2, "_FREL", c.Nature1.ToString());
                    Line(2, "_MREL", c.Nature2.ToString());
                }
            }
            var famFacts = FamilyFacts(fam).ToList();
            foreach (var f in famFacts) WriteFact(f, 1);
            if (fam.Notes.Length > 0 && !HidesCouple(fam)) Line(1, "NOTE", fam.Notes);
        }

        foreach (var s in sources)
        {
            Line(0, "SOUR", null, sourceRef[s.Id]);
            if (s.Title.Length > 0) Line(1, "TITL", s.Title);
            if (s.Author.Length > 0) Line(1, "AUTH", s.Author);
            var publ = string.Join(", ", new[] { s.Publisher, s.PublisherLocation, s.PublishDate }.Where(x => x.Length > 0));
            if (publ.Length > 0) Line(1, "PUBL", publ);
            if (s.RepositoryId is not null && repoRef.TryGetValue(s.RepositoryId, out var rr))
            {
                Line(1, "REPO", rr);
                if (s.CallNumber.Length > 0) Line(2, "CALN", s.CallNumber);
            }
            if (s.Comments.Length > 0) Line(1, "NOTE", s.Comments);
            if (s.Url.Length > 0) Line(1, "WWW", s.Url);
        }

        foreach (var r in repositories)
        {
            Line(0, "REPO", null, repoRef[r.Id]);
            Line(1, "NAME", r.Name);
            if (r.Address.Length > 0) Line(1, "ADDR", r.Address);
            if (r.Phone.Length > 0) Line(1, "PHON", r.Phone);
            if (r.Email.Length > 0) Line(1, "EMAIL", r.Email);
        }

        foreach (var m in media)
        {
            Line(0, "OBJE", null, mediaRef[m.Id]);
            Line(1, "FILE", m.FilePath);
            Line(2, "FORM", Path.GetExtension(m.FilePath).TrimStart('.').ToLowerInvariant());
            if (m.Caption.Length > 0) Line(2, "TITL", m.Caption);
            if (m.Date.Length > 0) Line(1, "DATE", GenDate.Parse(m.Date).ToGedcom());
        }

        Line(0, "TRLR");
        File.WriteAllText(path, w.ToString(), new UTF8Encoding(false));
    }

    private static (string, string, string) SplitAka(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "", "");
        if (parts.Length == 1) return (parts[0], "", "");
        return (string.Join(' ', parts[..^1]), parts[^1], "");
    }
}
