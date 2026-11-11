namespace Mycelia.Model;

/// <summary>
/// Whether a person is taken to be alive, for privatising exports.  The rule is in
/// Design considerations, Living people: no death date or place and no death-related fact; born within the
/// last 120 years and married within the last 100 where those dates are known; and at least a birth
/// date to go on.  Someone with nothing to go on counts as living when their preferred spouse or a
/// parent does.
/// </summary>
public static class Living
{
    public const int BirthYearsLimit = 120;
    public const int MarriageYearsLimit = 100;

    /// <summary>Burial, Cremation, Funeral and Cause of Death: any of them means the person has died.</summary>
    public static readonly IReadOnlySet<int> DeathRelatedFactTypes = new HashSet<int> { 20, 13, 31, 57 };

    public static bool IsLiving(TreeDocument doc, Person p) => IsLiving(doc, p, checkRelatives: true);

    private static bool IsLiving(TreeDocument doc, Person p, bool checkRelatives)
    {
        // The steps in the order Design considerations, Living people gives them.
        var death = p.Death;
        var noDeath = death is null || (death.ParsedDate.IsEmpty && death.PlaceId is null);
        if (!noDeath) return false;

        var today = DateTime.Today;
        var birthYear = p.Birth?.ParsedDate is { IsValid: true } b ? b.Year : null;
        var askRelatives = true;
        var living = birthYear is null || birthYear >= today.Year - BirthYearsLimit;
        if (living && SpouseFamily(doc, p)?.Marriage?.ParsedDate is { IsValid: true, Year: int my })
            living = my >= today.Year - MarriageYearsLimit;
        if (living)
        {
            askRelatives = !p.Facts.Any(f => DeathRelatedFactTypes.Contains(f.TypeId));
            // Living by themselves needs no death-related fact and a birth date to go on.
            living = askRelatives && birthYear is not null;
        }
        if (living) return true;
        if (!askRelatives || !checkRelatives) return false;

        var spouse = doc.GetPerson(SpouseFamily(doc, p)?.OtherPartner(p.Id));
        if (spouse is not null && IsLiving(doc, spouse, checkRelatives: false)) return true;
        return doc.Parents(p).Any(parent => IsLiving(doc, parent, checkRelatives: false));
    }

    private static Family? SpouseFamily(TreeDocument doc, Person p) => doc.SpouseFamilies(p).FirstOrDefault();
}
