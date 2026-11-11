namespace Mycelia.Model;

public enum FactScope { Person, Couple }

/// <summary>A kind of fact (Birth, Occupation, Marriage...).  See Design considerations, Fact types.</summary>
public sealed record FactType(int Id, string Name, string Tag, FactScope Scope, bool HasDate, bool HasPlace)
{
    public override string ToString() => Name;
}

public static class FactTypes
{
    public const int Birth = 2;
    public const int Death = 3;
    public const int Marriage = 4;
    public const int Baptism = 7;
    public const int Burial = 13;
    public const int Christening = 16;
    public const int Divorce = 22;
    public const int Occupation = 61;
    public const int Residence = 67;
    public const int AlsoKnownAs = 46;
    public const int Emigration = 25;
    public const int MilitaryService = 34;

    private static FactType Event(int id, string name, string tag) => new(id, name, tag, FactScope.Person, true, true);
    private static FactType Attribute(int id, string name, string tag) => new(id, name, tag, FactScope.Person, false, false);
    private static FactType Couple(int id, string name, string tag) => new(id, name, tag, FactScope.Couple, true, true);

    public static readonly IReadOnlyList<FactType> All =
    [
        Event(2, "Birth", "BIRT"),
        Event(3, "Death", "DEAT"),
        Event(5, "Adoption", "ADOP"),
        Event(7, "Baptism", "BAPM"),
        Event(9, "Bar Mitzvah", "BARM"),
        Event(11, "Bat Mitzvah", "BASM"),
        Event(12, "Blessing", "BLES"),
        Event(13, "Burial", "BURI"),
        Event(15, "Census", "CENS"),
        Event(16, "Christening", "CHR"),
        Event(66, "Christening (adult)", "CHRA"),
        Event(17, "Circumcision", "_CIRC"),
        Event(18, "Confirmation", "CONF"),
        Event(20, "Cremation", "CREM"),
        Event(21, "Degree", "_DEG"),
        Event(23, "Education", "EDUC"),
        Event(24, "Elected", "_ELEC"),
        Event(25, "Emigration", "EMIG"),
        Event(26, "Employment", "_EMPLOY"),
        Event(29, "Excommunication", "_EXCM"),
        Event(30, "First Communion", "FCOM"),
        Event(31, "Funeral", "_FUN"),
        Event(32, "Graduation", "GRAD"),
        Event(33, "Immigration", "IMMI"),
        Event(34, "Military Service", "_MILT"),
        Event(38, "Naturalization", "NATU"),
        Event(39, "Ordination", "ORDN"),
        Event(40, "Probate", "PROB"),
        Event(41, "Property", "PROP"),
        Event(42, "Retirement", "RETI"),
        Event(45, "Will", "WILL"),
        Event(49, "Address", "ADDR"),
        Event(50, "Email", "EMAIL"),
        Event(51, "Web Address", "_WEB"),
        Event(52, "Phone Number", "PHON"),
        Event(54, "Military Serial Number", "_MILTID"),
        Event(58, "Medical Condition", "_MDCL"),
        Event(59, "DNA Markers", "_DNA"),
        Event(61, "Occupation", "OCCU"),
        Event(67, "Residence", "RESI"),
        Event(83, "Physical Description", "DSCR"),
        Event(86, "Arrival", "ARVL"),
        Event(87, "Departure", "DPRT"),
        Attribute(46, "Also Known As", "ALIA"),
        Attribute(47, "Title", "TITL"),
        Attribute(14, "Caste", "CAST"),
        Attribute(53, "Person ID", "REFN"),
        Attribute(55, "Height", "_HEIG"),
        Attribute(56, "Weight", "_WEIG"),
        Attribute(57, "Cause of Death", "_DCAUSE"),
        Attribute(60, "Nationality", "NATI"),
        Attribute(62, "Religion", "RELI"),
        Attribute(65, "Namesake", "_NAMS"),
        Attribute(82, "Social Security Number", "SSN"),
        Attribute(89, "Destination", "_DEST"),
        Attribute(90, "Origin", "_ORIG"),
        Couple(4, "Marriage", "MARR"),
        Couple(6, "Annulment", "ANUL"),
        Couple(22, "Divorce", "DIV"),
        Couple(81, "Divorce Filed", "DIVF"),
        Couple(28, "Engagement", "ENGA"),
        Couple(44, "Separation", "_SEPR"),
        Couple(75, "Marriage License", "MARL"),
        Couple(76, "Marriage Contract", "MARC"),
        Couple(77, "Marriage Bann", "MARB"),
        Couple(78, "Marriage Settlement", "MARS"),
    ];

    private static readonly Dictionary<int, FactType> ById = All.ToDictionary(t => t.Id);

    /// <summary>First id given to a user-defined type; the built-in ids are all below it.</summary>
    public const int FirstCustomId = 1000;

    /// <summary>GEDCOM tag custom types are written with (with a TYPE line naming them).</summary>
    public const string CustomTag = "EVEN";

    private static Dictionary<int, FactType> _custom = [];
    private static IReadOnlyList<FactType> _personTypes = [];
    private static IReadOnlyList<FactType> _coupleTypes = [];

    static FactTypes() => SetCustom([]);

    /// <summary>The open tree's own fact types; called whenever the tree is loaded or changed.</summary>
    public static void SetCustom(IEnumerable<CustomFactType> custom)
    {
        _custom = custom.Where(c => c.Id >= FirstCustomId && c.Name.Trim().Length > 0)
            .GroupBy(c => c.Id).Select(g => g.First())
            .ToDictionary(c => c.Id, c => new FactType(c.Id, c.Name.Trim(), CustomTag, c.Scope, c.HasDate, c.HasPlace));
        var every = All.Concat(_custom.Values).ToList();
        _personTypes = every.Where(t => t.Scope == FactScope.Person).OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();
        _coupleTypes = every.Where(t => t.Scope == FactScope.Couple).OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();
    }

    public static bool IsCustom(int id) => id >= FirstCustomId;

    public static IReadOnlyList<FactType> PersonTypes => _personTypes;

    public static IReadOnlyList<FactType> CoupleTypes => _coupleTypes;

    public static FactType Get(int id) =>
        ById.TryGetValue(id, out var t) ? t
        : _custom.TryGetValue(id, out var c) ? c
        : new FactType(id, "Fact", "EVEN", FactScope.Person, true, true);

    public static FactType? ByTag(string tag, FactScope scope) =>
        All.FirstOrDefault(t => t.Scope == scope && string.Equals(t.Tag, tag, StringComparison.OrdinalIgnoreCase));
}
