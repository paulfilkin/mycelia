using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mycelia.Model;

/// <summary>
/// The open tree: its data, quick lookups, family queries, changes with undo, and saving.
/// </summary>
public sealed class TreeDocument
{
    public const string FileExtension = ".ftree";
    private const int UndoLimit = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The serialiser settings, for copies of the tree (backups) that must read back the same way.</summary>
    public static JsonSerializerOptions JsonOptionsForCopy => JsonOptions;

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private Dictionary<string, Person> _people = [];
    private Dictionary<string, Family> _families = [];
    private Dictionary<string, Place> _places = [];
    private Dictionary<string, Source> _sources = [];
    private Dictionary<string, Repository> _repositories = [];
    private Dictionary<string, MediaItem> _media = [];
    private object? _editToken;

    public TreeDocument(TreeData data)
    {
        Data = data;
        RebuildIndex();
    }

    public TreeData Data { get; private set; }
    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>People, families or links were added, removed or replaced.</summary>
    public event Action? StructureChanged;

    /// <summary>Something was edited (dirty flag, undo state).</summary>
    public event Action? Edited;

    public string Title => FilePath is null ? Data.Name : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>
    /// The folder beside the tree file for documents and pictures downloaded for it ("Ashworth media" beside
    /// "Ashworth.ftree"), so the tree and its files move and are backed up together (TDD 25); null for a tree never
    /// saved.  Made when first needed; nothing in it is ever deleted by the program.
    /// </summary>
    public string? MediaFolder => FilePath is null ? null : Path.Combine(Path.GetDirectoryName(FilePath)!, Path.GetFileNameWithoutExtension(FilePath) + " media");

    // ---- lookups ----

    public Person? GetPerson(string? id) => id is not null && _people.TryGetValue(id, out var p) ? p : null;
    public Family? GetFamily(string? id) => id is not null && _families.TryGetValue(id, out var f) ? f : null;
    public Place? GetPlace(string? id) => id is not null && _places.TryGetValue(id, out var p) ? p : null;
    public Source? GetSource(string? id) => id is not null && _sources.TryGetValue(id, out var s) ? s : null;
    public Repository? GetRepository(string? id) => id is not null && _repositories.TryGetValue(id, out var r) ? r : null;
    public MediaItem? GetMedia(string? id) => id is not null && _media.TryGetValue(id, out var m) ? m : null;

    public Person? HomePerson => GetPerson(Data.HomePersonId) ?? Data.People.FirstOrDefault();

    public string PlaceName(string? placeId) => GetPlace(placeId)?.Name ?? "";

    // ---- family queries ----

    /// <summary>The families listing this person as a child, the preferred parents first, the rest in stored order.</summary>
    public IEnumerable<Family> ParentFamilies(Person p)
    {
        var families = Data.Families.Where(f => f.Children.Any(c => c.PersonId == p.Id)).ToList();
        if (p.PreferredParentsId is null || families.Count < 2) return families;
        return families.OrderBy(f => f.Id == p.PreferredParentsId ? 0 : 1);
    }

    public Family? ParentFamily(Person p) => ParentFamilies(p).FirstOrDefault();

    /// <summary>A person's couples in their spouse order (Set spouse order), the preferred first; the rest in stored order.</summary>
    public IEnumerable<Family> SpouseFamilies(Person p)
    {
        var families = Data.Families.Where(f => f.HasPartner(p.Id)).ToList();
        if (p.SpouseOrder.Count == 0 || families.Count < 2) return families;
        return families.OrderBy(f => { var i = p.SpouseOrder.IndexOf(f.Id); return i < 0 ? int.MaxValue : i; });
    }

    public Person? Father(Person p) => ParentFamily(p) is { } f ? PartnerBySex(f, Sex.Male, 1) : null;
    public Person? Mother(Person p) => ParentFamily(p) is { } f ? PartnerBySex(f, Sex.Female, 2) : null;

    /// <summary>The partner of the given sex; falls back to slot order when sexes are unknown.</summary>
    public Person? PartnerBySex(Family f, Sex sex, int slot)
    {
        var a = GetPerson(f.Partner1Id);
        var b = GetPerson(f.Partner2Id);
        if (a?.Sex == sex) return a;
        if (b?.Sex == sex) return b;
        var other = sex == Sex.Male ? Sex.Female : Sex.Male;
        if (a is not null && a.Sex != other && slot == 1) return a;
        if (b is not null && b.Sex != other && slot == 2) return b;
        return null;
    }

    public IEnumerable<Person> Parents(Person p) =>
        ParentFamilies(p).SelectMany(f => new[] { GetPerson(f.Partner1Id), GetPerson(f.Partner2Id) }).OfType<Person>().Distinct();

    public IEnumerable<Person> Spouses(Person p) =>
        SpouseFamilies(p).Select(f => GetPerson(f.OtherPartner(p.Id))).OfType<Person>();

    public IEnumerable<Person> Children(Family f) =>
        f.Children.Select(c => GetPerson(c.PersonId)).OfType<Person>();

    public IEnumerable<Person> Children(Person p) =>
        SpouseFamilies(p).SelectMany(Children).Distinct();

    public IEnumerable<Person> Siblings(Person p) =>
        ParentFamilies(p).SelectMany(Children).Where(c => c != p).Distinct();

    /// <summary>
    /// Children of each parent's other families, who share one parent with this person and are not also full siblings
    /// (the Family tab lists them after the siblings, with kinship's "half" wording).
    /// </summary>
    public IEnumerable<Person> HalfSiblings(Person p)
    {
        var own = ParentFamilies(p).ToHashSet();
        var siblings = Siblings(p).ToHashSet();
        return Parents(p).SelectMany(parent => SpouseFamilies(parent).Where(f => !own.Contains(f)))
            .SelectMany(Children).Where(c => c != p && !siblings.Contains(c)).Distinct();
    }

    public IEnumerable<Person> PeopleAtPlace(Place place) =>
        Data.People.Where(p => p.Facts.Any(f => f.PlaceId == place.Id));

    public IEnumerable<(object Owner, Fact Fact)> AllFacts()
    {
        foreach (var p in Data.People)
            foreach (var f in p.Facts)
                yield return (p, f);
        foreach (var fam in Data.Families)
            foreach (var f in fam.Facts)
                yield return (fam, f);
    }

    public string OwnerName(object owner) => owner switch
    {
        Person p => p.DisplayName,
        Family f => CoupleName(f),
        _ => "",
    };

    public string CoupleName(Family f)
    {
        var a = GetPerson(f.Partner1Id)?.DisplayName;
        var b = GetPerson(f.Partner2Id)?.DisplayName;
        return (a, b) switch
        {
            (null, null) => "Unknown couple",
            (null, _) => b!,
            (_, null) => a,
            _ => $"{a} & {b}",
        };
    }

    // ---- changes ----

    /// <summary>Runs a change as one undo step.</summary>
    public void Change(Action change, bool structural = true)
    {
        PushUndo();
        change();
        _editToken = null;
        AfterChange(structural);
    }

    /// <summary>
    /// Called before typing into a field: the first edit with a new token becomes an undo step,
    /// later keystrokes with the same token join it.
    /// </summary>
    public void BeginEdit(object token)
    {
        if (ReferenceEquals(_editToken, token)) return;
        _editToken = token;
        PushUndo();
        Edited?.Invoke();
    }

    /// <summary>A field was edited in place (after <see cref="BeginEdit"/>).</summary>
    public void MarkEdited()
    {
        IsDirty = true;
        Data.Modified = DateTime.Now;
        Edited?.Invoke();
    }

    public void EndEdit() => _editToken = null;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Serialize());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Serialize());
        Restore(_redo.Pop());
    }

    /// <summary>
    /// Puts the whole tree back to a copy taken with <see cref="Serialize"/> (the Assistant's Revert this request), as
    /// one undo step, so Undo brings the reverted changes back.  Bookmarks stay as they are, as with Undo.
    /// </summary>
    public void RestoreCopy(string json)
    {
        Change(() =>
        {
            var bookmarks = Data.Bookmarks;
            Data = JsonSerializer.Deserialize<TreeData>(json, JsonOptions) ?? new TreeData();
            Data.Bookmarks = bookmarks;
        });
    }

    private void PushUndo()
    {
        _undo.Push(Serialize());
        _redo.Clear();
        if (_undo.Count > UndoLimit)
        {
            var keep = _undo.Take(UndoLimit).Reverse().ToList();
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
    }

    private void Restore(string json)
    {
        // Bookmarks are not undo steps (AppState.ToggleBookmark), so the current list is kept rather
        // than the one in the snapshot; ids of people the snapshot lacks are skipped where listed.
        var bookmarks = Data.Bookmarks;
        Data = JsonSerializer.Deserialize<TreeData>(json, JsonOptions) ?? new TreeData();
        Data.Bookmarks = bookmarks;
        _editToken = null;
        AfterChange(true);
    }

    private void AfterChange(bool structural)
    {
        IsDirty = true;
        Data.Modified = DateTime.Now;
        RebuildIndex();
        if (structural) StructureChanged?.Invoke();
        Edited?.Invoke();
    }

    public void RebuildIndex()
    {
        FactTypes.SetCustom(Data.CustomFactTypes);
        _people = Data.People.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        _families = Data.Families.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        _places = Data.Places.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        _sources = Data.Sources.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _repositories = Data.Repositories.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        _media = Data.Media.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
    }

    // ---- building blocks for edits (call inside Change) ----

    public Person AddPerson(string given, string surname, Sex sex)
    {
        var p = new Person { Given = given.Trim(), Surname = surname.Trim(), Sex = sex };
        Data.People.Add(p);
        _people[p.Id] = p;
        Data.HomePersonId ??= p.Id;
        return p;
    }

    public Fact AddFact(List<Fact> facts, int typeId, string date, string place, string description = "")
    {
        var fact = new Fact
        {
            TypeId = typeId,
            Date = date.Trim(),
            PlaceId = GetOrCreatePlace(place)?.Id,
            Description = description.Trim(),
            Preferred = !facts.Any(f => f.TypeId == typeId),
        };
        facts.Add(fact);
        return fact;
    }

    public Place? GetOrCreatePlace(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return null;
        var existing = Data.Places.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null) return existing;
        var place = new Place { Name = n };
        Data.Places.Add(place);
        _places[place.Id] = place;
        return place;
    }

    public Family AddFamily(Person? a, Person? b)
    {
        // Partner 1 is the man where known, as GEDCOM HUSB.
        if (a is not null && b is not null && a.Sex == Sex.Female && b.Sex != Sex.Female) (a, b) = (b, a);
        var f = new Family { Partner1Id = a?.Id, Partner2Id = b?.Id };
        Data.Families.Add(f);
        _families[f.Id] = f;
        return f;
    }

    public void AddChild(Family family, Person child, ChildNature nature = ChildNature.Natural)
    {
        if (family.Children.Any(c => c.PersonId == child.Id)) return;
        family.Children.Add(new ChildLink { PersonId = child.Id, Nature1 = nature, Nature2 = nature });
    }

    /// <summary>The family holding this person's parents, created when missing.</summary>
    public Family EnsureParentFamily(Person child)
    {
        var f = ParentFamily(child);
        if (f is not null) return f;
        f = AddFamily(null, null);
        AddChild(f, child);
        return f;
    }

    /// <summary>
    /// The family for a couple, by one rule for every way of linking spouses, in the program and the Assistant (TDD 7):
    /// their own family when they already have one, so a couple is never recorded twice; otherwise
    /// <paramref name="open"/> when it is one of the person's families with a partner missing (an Unknown spouse card's
    /// own buttons), or else the first such family in spouse order, so its children gain the new parent; otherwise a new
    /// family.
    /// </summary>
    public Family LinkCouple(Person person, Person spouse, Family? open = null)
    {
        if (SpouseFamilies(person).FirstOrDefault(f => f.HasPartner(spouse.Id)) is { } existing) return existing;
        var fill = open is not null && open.HasPartner(person.Id) && IsOpen(open) ? open : SpouseFamilies(person).FirstOrDefault(IsOpen);
        if (fill is null) return AddFamily(person, spouse);
        if (fill.Partner1Id is null) fill.Partner1Id = spouse.Id; else fill.Partner2Id = spouse.Id;
        // Partner 1 is the man where known, as AddFamily does; each child's relationships follow their partner.
        if (GetPerson(fill.Partner1Id) is { Sex: Sex.Female } && GetPerson(fill.Partner2Id) is { Sex: not Sex.Female })
        {
            (fill.Partner1Id, fill.Partner2Id) = (fill.Partner2Id, fill.Partner1Id);
            foreach (var c in fill.Children) (c.Nature1, c.Nature2) = (c.Nature2, c.Nature1);
        }
        return fill;
    }

    /// <summary>A family with one partner slot or both empty.</summary>
    public static bool IsOpen(Family f) => f.Partner1Id is null || f.Partner2Id is null;

    /// <summary>
    /// Places a parent into the child's parent family.  When that family has a parent missing and the parent already in
    /// it is the new parent's partner in another family, the child moves into that couple's family instead (keeping how
    /// they are related to the parent already linked), so the couple isn't recorded twice; the family left behind goes
    /// when it is empty (<see cref="RemoveIfEmpty"/>).  Brothers and sisters in the old family stay, since they may have
    /// another parent.
    /// </summary>
    public void SetParent(Person child, Person parent)
    {
        if (ParentFamilies(child).Any(x => x.HasPartner(parent.Id))) return;
        var f = EnsureParentFamily(child);
        if (IsOpen(f) && GetPerson(f.Partner1Id ?? f.Partner2Id) is { } other
            && SpouseFamilies(other).FirstOrDefault(x => x != f && x.HasPartner(parent.Id)) is { } couple)
        {
            var link = f.Children.First(c => c.PersonId == child.Id);
            var toOther = f.Partner1Id == other.Id ? link.Nature1 : link.Nature2;
            f.Children.Remove(link);
            if (child.PreferredParentsId == f.Id) child.PreferredParentsId = couple.Id;
            couple.Children.Add(new ChildLink
            {
                PersonId = child.Id,
                Nature1 = couple.Partner1Id == other.Id ? toOther : ChildNature.Natural,
                Nature2 = couple.Partner2Id == other.Id ? toOther : ChildNature.Natural,
            });
            RemoveIfEmpty(f);
            return;
        }
        if (f.Partner1Id is null && (parent.Sex != Sex.Female || f.Partner2Id is not null)) f.Partner1Id = parent.Id;
        else if (f.Partner2Id is null) f.Partner2Id = parent.Id;
        else
        {
            var nf = AddFamily(parent, null);
            AddChild(nf, child);
        }
    }

    public void RemovePerson(Person p)
    {
        Data.People.Remove(p);
        foreach (var f in Data.Families)
        {
            if (f.Partner1Id == p.Id) f.Partner1Id = null;
            if (f.Partner2Id == p.Id) f.Partner2Id = null;
            f.Children.RemoveAll(c => c.PersonId == p.Id);
        }
        Data.Families.RemoveAll(f => f.Partner1Id is null && f.Partner2Id is null && f.Children.Count == 0
                                     || (f.Partner1Id is null ^ f.Partner2Id is null) && f.Children.Count == 0 && f.Facts.Count == 0);
        foreach (var m in Data.Media) m.PersonIds.Remove(p.Id);
        if (Data.HomePersonId == p.Id) Data.HomePersonId = Data.People.FirstOrDefault()?.Id;
    }

    public void DetachChild(Family f, Person child)
    {
        f.Children.RemoveAll(c => c.PersonId == child.Id);
        if (child.PreferredParentsId == f.Id) child.PreferredParentsId = null;
        RemoveIfEmpty(f);
    }

    public void DetachPartner(Family f, Person partner)
    {
        if (f.Partner1Id == partner.Id) f.Partner1Id = null;
        if (f.Partner2Id == partner.Id) f.Partner2Id = null;
        RemoveIfEmpty(f);
    }

    /// <summary>
    /// Removes a family with no children and no partners, or with one partner and no facts either: no family at all (as
    /// RemovePerson leaves it); otherwise it lingers as an "Unknown spouse" card the person panel can't remove.
    /// </summary>
    private void RemoveIfEmpty(Family f)
    {
        if (f.Children.Count == 0 && (f.Partner1Id is null && f.Partner2Id is null || IsOpen(f) && f.Facts.Count == 0))
            Data.Families.Remove(f);
    }

    /// <summary>Removes places no fact uses any more.</summary>
    public void PrunePlaces()
    {
        var used = AllFacts().Select(x => x.Fact.PlaceId).OfType<string>().ToHashSet();
        Data.Places.RemoveAll(p => !used.Contains(p.Id) && p.Latitude.Length == 0 && p.Notes.Length == 0);
    }

    // ---- files ----

    public string Serialize() => JsonSerializer.Serialize(Data, JsonOptions);

    public static TreeDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<TreeData>(json, JsonOptions) ?? new TreeData();
        return new TreeDocument(data) { FilePath = path };
    }

    public void Save(string path)
    {
        Data.Modified = DateTime.Now;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize());
        File.Move(tmp, path, overwrite: true);
        FilePath = path;
        IsDirty = false;
        Edited?.Invoke();
    }

    /// <summary>Statistics for the home page.</summary>
    public (int People, int Families, int Places, int Sources, int Media, int Surnames) Counts() =>
        (Data.People.Count, Data.Families.Count, Data.Places.Count, Data.Sources.Count, Data.Media.Count,
         Data.People.Select(p => p.Surname).Where(s => s.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
}
