using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Mycelia.Model;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tells every binding to read this object again.</summary>
    public void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public enum Sex { Male, Female, Unknown }

/// <summary>Couple types (Design considerations, Enumerations).</summary>
public enum CoupleType { Spouse, Partner, Friend, Single, Other, Unknown, Private }

/// <summary>Couple status (Design considerations, Enumerations).</summary>
public enum CoupleStatus { Unknown, Ongoing, Divorced, Deceased, Separated, Annulled, Private, Other, None }

/// <summary>How a child is related to a parent (Design considerations, Enumerations).</summary>
public enum ChildNature { Natural, Adopted, Step, Foster, Related, Guardian, Sealed, Private, Unknown }

public static class Ids
{
    public static string New(char prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
}

/// <summary>A link from a fact to a source, with where in the source it was found.</summary>
public sealed class Citation : Observable
{
    private string _detail = "";
    private string _sourceId = "";
    private string _url = "";
    private string _accessed = "";
    private string _text = "";

    public string SourceId { get => _sourceId; set => Set(ref _sourceId, value); }

    /// <summary>Page, entry or other detail within the source.</summary>
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    /// <summary>The web address of the record itself, when it was found online (TDD 25).</summary>
    public string Url { get => _url; set => Set(ref _url, value ?? ""); }

    /// <summary>When the web address was looked at, as typed (a date).</summary>
    public string Accessed { get => _accessed; set => Set(ref _accessed, value ?? ""); }

    /// <summary>A transcription or extract of what the record says.</summary>
    public string Text { get => _text; set => Set(ref _text, value ?? ""); }
}

public sealed class Fact : Observable
{
    private int _typeId;
    private string _date = "";
    private string? _placeId;
    private string _description = "";
    private bool _preferred = true;
    private bool _private;
    private string _note = "";

    public string Id { get; set; } = Ids.New('E');

    public int TypeId
    {
        get => _typeId;
        set { if (Set(ref _typeId, value)) Raise(nameof(Type)); }
    }

    public string Date
    {
        get => _date;
        set { if (Set(ref _date, value ?? "")) Raise(nameof(ParsedDate)); }
    }

    public string? PlaceId { get => _placeId; set => Set(ref _placeId, value); }
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }
    public bool Preferred { get => _preferred; set => Set(ref _preferred, value); }
    public bool Private { get => _private; set => Set(ref _private, value); }
    public string Note { get => _note; set => Set(ref _note, value ?? ""); }
    public List<Citation> Citations { get; set; } = [];

    [JsonIgnore] public FactType Type => FactTypes.Get(TypeId);
    [JsonIgnore] public GenDate ParsedDate => GenDate.Parse(Date);
}

public sealed class Person : Observable
{
    private string _given = "";
    private string _surname = "";
    private string _suffix = "";
    private Sex _sex = Sex.Unknown;
    private string _notes = "";
    private string? _photoMediaId;
    private bool _private;

    public string Id { get; set; } = Ids.New('I');

    public string Given { get => _given; set { if (Set(ref _given, value ?? "")) RaiseNames(); } }
    public string Surname { get => _surname; set { if (Set(ref _surname, value ?? "")) RaiseNames(); } }
    public string Suffix { get => _suffix; set { if (Set(ref _suffix, value ?? "")) RaiseNames(); } }
    public Sex Sex { get => _sex; set => Set(ref _sex, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value ?? ""); }
    public string? PhotoMediaId { get => _photoMediaId; set => Set(ref _photoMediaId, value); }
    public bool Private { get => _private; set => Set(ref _private, value); }
    public List<Fact> Facts { get; set; } = [];

    /// <summary>
    /// Ids of this person's couples in the order chosen with Set spouse order; the first is the
    /// preferred spouse.  Couples not listed follow in stored order.
    /// </summary>
    public List<string> SpouseOrder { get; set; } = [];

    /// <summary>Show surnames in capitals wherever names are displayed (Design considerations, Names).</summary>
    public static bool SurnamesInCapitals { get; set; }

    private string ShownSurname => SurnamesInCapitals ? Surname.ToUpper(System.Globalization.CultureInfo.CurrentCulture) : Surname;

    /// <summary>"Given Surname Suffix", or "&lt;No name&gt;" (Design considerations, Names).</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { Given, ShownSurname, Suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return name.Length == 0 ? "<No name>" : name;
        }
    }

    /// <summary>"Surname, Given Suffix" for the index (Design considerations, Names).</summary>
    [JsonIgnore]
    public string IndexName
    {
        get
        {
            var given = string.Join(' ', new[] { Given, Suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (Surname.Length == 0 && given.Length == 0) return "<No name>";
            if (Surname.Length == 0) return given;
            return given.Length == 0 ? ShownSurname : $"{ShownSurname}, {given}";
        }
    }

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var a = Given.Length > 0 ? Given[0].ToString() : "";
            var b = Surname.Length > 0 ? Surname[0].ToString() : "";
            var s = (a + b).ToUpperInvariant();
            return s.Length == 0 ? "?" : s;
        }
    }

    [JsonIgnore] public Fact? Birth => Preferred(FactTypes.Birth);
    [JsonIgnore] public Fact? Death => Preferred(FactTypes.Death);

    /// <summary>"1850-1920", "1850-", "-1920" or empty.</summary>
    [JsonIgnore]
    public string LifeSpan
    {
        get
        {
            var b = Birth?.ParsedDate.Year;
            var d = Death?.ParsedDate.Year;
            var hasDeath = Death is not null;
            if (b is null && d is null) return hasDeath ? "Deceased" : "";
            return $"{b?.ToString() ?? ""}-{d?.ToString() ?? ""}";
        }
    }

    public Fact? Preferred(int typeId) =>
        Facts.FirstOrDefault(f => f.TypeId == typeId && f.Preferred) ?? Facts.FirstOrDefault(f => f.TypeId == typeId);

    public void RaiseFactsChanged()
    {
        Raise(nameof(Birth));
        Raise(nameof(Death));
        Raise(nameof(LifeSpan));
    }

    private void RaiseNames()
    {
        Raise(nameof(DisplayName));
        Raise(nameof(IndexName));
        Raise(nameof(Initials));
    }

    public override string ToString() => DisplayName;
}

public sealed class ChildLink : Observable
{
    private ChildNature _nature1 = ChildNature.Natural;
    private ChildNature _nature2 = ChildNature.Natural;

    public string PersonId { get; set; } = "";

    /// <summary>Relationship to the first partner.</summary>
    public ChildNature Nature1 { get => _nature1; set => Set(ref _nature1, value); }

    /// <summary>Relationship to the second partner.</summary>
    public ChildNature Nature2 { get => _nature2; set => Set(ref _nature2, value); }
}

/// <summary>A couple (or a single parent) and their children.</summary>
public sealed class Family : Observable
{
    private string? _partner1Id;
    private string? _partner2Id;
    private CoupleType _type = CoupleType.Spouse;
    private CoupleStatus _status = CoupleStatus.Ongoing;
    private string _notes = "";

    public string Id { get; set; } = Ids.New('F');
    public string? Partner1Id { get => _partner1Id; set => Set(ref _partner1Id, value); }
    public string? Partner2Id { get => _partner2Id; set => Set(ref _partner2Id, value); }
    public CoupleType Type { get => _type; set => Set(ref _type, value); }
    public CoupleStatus Status { get => _status; set => Set(ref _status, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value ?? ""); }
    public List<Fact> Facts { get; set; } = [];
    public List<ChildLink> Children { get; set; } = [];

    [JsonIgnore] public Fact? Marriage => Facts.FirstOrDefault(f => f.TypeId == FactTypes.Marriage && f.Preferred) ?? Facts.FirstOrDefault(f => f.TypeId == FactTypes.Marriage);

    public bool HasPartner(string id) => Partner1Id == id || Partner2Id == id;
    public string? OtherPartner(string id) => Partner1Id == id ? Partner2Id : Partner2Id == id ? Partner1Id : null;
}

public sealed class Place : Observable
{
    private string _name = "";
    private string _latitude = "";
    private string _longitude = "";
    private string _notes = "";

    public string Id { get; set; } = Ids.New('L');
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Latitude { get => _latitude; set => Set(ref _latitude, value ?? ""); }
    public string Longitude { get => _longitude; set => Set(ref _longitude, value ?? ""); }
    public string Notes { get => _notes; set => Set(ref _notes, value ?? ""); }

    /// <summary>The most local part, e.g. "Bristol" from "Bristol, Gloucestershire, England".</summary>
    [JsonIgnore] public string ShortName => Name.Split(',')[0].Trim();

    public override string ToString() => Name;
}

public sealed class Repository : Observable
{
    private string _name = "";
    private string _address = "";
    private string _phone = "";
    private string _email = "";

    public string Id { get; set; } = Ids.New('R');
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Address { get => _address; set => Set(ref _address, value ?? ""); }
    public string Phone { get => _phone; set => Set(ref _phone, value ?? ""); }
    public string Email { get => _email; set => Set(ref _email, value ?? ""); }

    public override string ToString() => Name;
}

public sealed class Source : Observable
{
    private string _title = "";
    private string _author = "";
    private string _publisher = "";
    private string _publisherLocation = "";
    private string _publishDate = "";
    private string? _repositoryId;
    private string _callNumber = "";
    private string _comments = "";
    private string _url = "";

    public string Id { get; set; } = Ids.New('S');
    public string Title { get => _title; set => Set(ref _title, value ?? ""); }
    public string Author { get => _author; set => Set(ref _author, value ?? ""); }
    public string Publisher { get => _publisher; set => Set(ref _publisher, value ?? ""); }
    public string PublisherLocation { get => _publisherLocation; set => Set(ref _publisherLocation, value ?? ""); }
    public string PublishDate { get => _publishDate; set => Set(ref _publishDate, value ?? ""); }
    public string? RepositoryId { get => _repositoryId; set => Set(ref _repositoryId, value); }
    public string CallNumber { get => _callNumber; set => Set(ref _callNumber, value ?? ""); }
    public string Comments { get => _comments; set => Set(ref _comments, value ?? ""); }

    /// <summary>The source's web address: an online database, catalogue entry or digitised book (TDD 25).</summary>
    public string Url { get => _url; set => Set(ref _url, value ?? ""); }

    public override string ToString() => Title.Length == 0 ? "(untitled source)" : Title;
}

public sealed class MediaItem : Observable
{
    private string _filePath = "";
    private string _caption = "";
    private string _date = "";
    private string _description = "";
    private string _category = "";

    public string Id { get; set; } = Ids.New('M');
    public string FilePath { get => _filePath; set => Set(ref _filePath, value ?? ""); }
    public string Caption { get => _caption; set => Set(ref _caption, value ?? ""); }
    public string Date { get => _date; set => Set(ref _date, value ?? ""); }
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }
    public string Category { get => _category; set => Set(ref _category, value ?? ""); }
    public List<string> PersonIds { get; set; } = [];

    [JsonIgnore] public bool FileExists => File.Exists(FilePath);

    [JsonIgnore]
    public bool IsImage => Path.GetExtension(FilePath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff";
}

/// <summary>Everything saved in a tree file.</summary>
public sealed class TreeData
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "My family tree";
    public string? HomePersonId { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
    public List<Person> People { get; set; } = [];
    public List<Family> Families { get; set; } = [];
    public List<Place> Places { get; set; } = [];
    public List<Source> Sources { get; set; } = [];
    public List<Repository> Repositories { get; set; } = [];
    public List<MediaItem> Media { get; set; } = [];

    /// <summary>Ids of people viewed, oldest first (see IndexSort.HistoryLimit).</summary>
    public List<string> History { get; set; } = [];

    /// <summary>Ids of bookmarked people, in the order they were added.</summary>
    public List<string> Bookmarks { get; set; } = [];

    /// <summary>Fact types the user has added (Edit > Manage fact types).</summary>
    public List<CustomFactType> CustomFactTypes { get; set; } = [];

    /// <summary>Historical events shown on timelines (Edit > Manage historical events).</summary>
    public List<HistoricalEvent> HistoricalEvents { get; set; } = [];
}

/// <summary>A fact type the user defined.  Ids start at <see cref="FactTypes.FirstCustomId"/>.</summary>
public sealed class CustomFactType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FactScope Scope { get; set; } = FactScope.Person;
    public bool HasDate { get; set; } = true;
    public bool HasPlace { get; set; } = true;
}

/// <summary>Something that happened in the world, for timelines.</summary>
public sealed class HistoricalEvent : Observable
{
    private string _date = "";
    private string _endDate = "";
    private string _title = "";
    private string _region = "";
    private string _category = "";
    private string _description = "";

    public string Id { get; set; } = Ids.New('H');
    public string Date { get => _date; set => Set(ref _date, value ?? ""); }

    /// <summary>When the event lasted a while: its end, else empty.</summary>
    public string EndDate { get => _endDate; set => Set(ref _endDate, value ?? ""); }

    public string Title { get => _title; set => Set(ref _title, value ?? ""); }

    /// <summary>Where it applies ("United Kingdom", "Ireland", "World").</summary>
    public string Region { get => _region; set => Set(ref _region, value ?? ""); }

    public string Category { get => _category; set => Set(ref _category, value ?? ""); }
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }

    public override string ToString() => Title;
}
