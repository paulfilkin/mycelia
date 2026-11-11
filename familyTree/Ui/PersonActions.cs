using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>Adding, linking and removing relatives, each as one undo step.</summary>
public static class PersonActions
{
    private static Person Create(TreeDocument doc, NewPersonInfo info)
    {
        var p = doc.AddPerson(info.Given, info.Surname, info.Sex);
        if (info.BirthDate.Length > 0 || info.BirthPlace.Length > 0)
            doc.AddFact(p.Facts, FactTypes.Birth, info.BirthDate, info.BirthPlace);
        if (info.DeathDate.Length > 0 || info.DeathPlace.Length > 0)
            doc.AddFact(p.Facts, FactTypes.Death, info.DeathDate, info.DeathPlace);
        return p;
    }

    public static void AddUnrelated(AppState state)
    {
        var info = Dialogs.NewPerson(state.Doc, "Add person", "Someone not yet linked to the tree.", "", Sex.Unknown);
        if (info is null) return;
        Person? created = null;
        state.Doc.Change(() => created = Create(state.Doc, info));
        state.Select(created, focus: true);
    }

    public static void AddParent(AppState state, Person child, Sex sex)
    {
        var role = sex == Sex.Male ? "father" : "mother";
        var surname = sex == Sex.Male ? child.Surname : "";
        var info = Dialogs.NewPerson(state.Doc, $"Add {role}", $"{role[..1].ToUpperInvariant()}{role[1..]} of {child.DisplayName}", surname, sex);
        if (info is null) return;
        Person? created = null;
        state.Doc.Change(() =>
        {
            created = Create(state.Doc, info);
            state.Doc.SetParent(child, created);
        });
        state.Select(created);
    }

    public static void AddSpouse(AppState state, Person person)
    {
        var sex = person.Sex switch { Sex.Male => Sex.Female, Sex.Female => Sex.Male, _ => Sex.Unknown };
        var info = Dialogs.NewPerson(state.Doc, "Add spouse", $"Spouse or partner of {person.DisplayName}", "", sex);
        if (info is null) return;
        Person? created = null;
        state.Doc.Change(() =>
        {
            created = Create(state.Doc, info);
            // Reuse a single-parent family when there is one, so existing children gain the new parent.
            var open = state.Doc.SpouseFamilies(person).FirstOrDefault(f => f.Partner1Id is null || f.Partner2Id is null);
            if (open is not null)
            {
                if (open.Partner1Id is null) open.Partner1Id = created.Id; else open.Partner2Id = created.Id;
            }
            else
            {
                state.Doc.AddFamily(person, created);
            }
        });
        state.Select(created);
    }

    public static void AddChild(AppState state, Person parent, Family? family = null)
    {
        family ??= state.Doc.SpouseFamilies(parent).FirstOrDefault();
        var surname = parent.Sex == Sex.Male ? parent.Surname
            : state.Doc.GetPerson(family?.OtherPartner(parent.Id)) is { Sex: Sex.Male } dad ? dad.Surname : parent.Surname;
        var sub = family is not null ? $"Child of {state.Doc.CoupleName(family)}" : $"Child of {parent.DisplayName}";
        var info = Dialogs.NewPerson(state.Doc, "Add child", sub, surname, Sex.Unknown);
        if (info is null) return;
        Person? created = null;
        state.Doc.Change(() =>
        {
            created = Create(state.Doc, info);
            var fam = family ?? state.Doc.AddFamily(parent, null);
            state.Doc.AddChild(fam, created);
        });
        state.Select(created);
    }

    public static void AddSibling(AppState state, Person person)
    {
        var info = Dialogs.NewPerson(state.Doc, "Add sibling", $"Brother or sister of {person.DisplayName}", person.Surname, Sex.Unknown);
        if (info is null) return;
        Person? created = null;
        state.Doc.Change(() =>
        {
            created = Create(state.Doc, info);
            var fam = state.Doc.EnsureParentFamily(person);
            state.Doc.AddChild(fam, created);
        });
        state.Select(created);
    }

    public static void LinkExistingParent(AppState state, Person child)
    {
        var exclude = state.Doc.Parents(child).Append(child);
        var parent = Dialogs.PickPerson(state.Doc, "Link a parent", $"Choose a parent for {child.DisplayName}", exclude);
        if (parent is null) return;
        state.Doc.Change(() => state.Doc.SetParent(child, parent));
    }

    public static void LinkExistingSpouse(AppState state, Person person)
    {
        var exclude = state.Doc.Spouses(person).Append(person);
        var spouse = Dialogs.PickPerson(state.Doc, "Link a spouse", $"Choose a spouse or partner for {person.DisplayName}", exclude);
        if (spouse is null) return;
        state.Doc.Change(() => state.Doc.AddFamily(person, spouse));
    }

    public static void LinkExistingChild(AppState state, Person parent, Family? family)
    {
        var exclude = state.Doc.Children(parent).Append(parent);
        var child = Dialogs.PickPerson(state.Doc, "Link a child", $"Choose a child of {parent.DisplayName}", exclude);
        if (child is null) return;
        state.Doc.Change(() =>
        {
            var fam = family ?? state.Doc.SpouseFamilies(parent).FirstOrDefault() ?? state.Doc.AddFamily(parent, null);
            state.Doc.AddChild(fam, child);
        });
    }

    public static void Delete(AppState state, Person person)
    {
        if (!Dialogs.Confirm("Delete person", $"Delete {person.DisplayName} and all of their facts?  Their relatives stay in the tree.", "Delete")) return;
        var next = state.Doc.Parents(person).FirstOrDefault() ?? state.Doc.Spouses(person).FirstOrDefault() ?? state.Doc.Children(person).FirstOrDefault();
        state.Doc.Change(() => state.Doc.RemovePerson(person));
        state.Select(next ?? state.Doc.HomePerson, focus: true);
    }

    /// <summary>Merge two records of the same person; either may be chosen in the dialog.</summary>
    public static void Merge(AppState state, Person? first, Person? second)
    {
        var pair = Dialogs.MergePeople(state.Doc, first, second);
        if (pair is null) return;
        var (keep, merge) = pair.Value;
        var keepId = keep.Id;
        state.Doc.Change(() => PersonMerge.Merge(state.Doc, keep, merge));
        state.Select(state.Doc.GetPerson(keepId), focus: true);
    }

    public static void SetHome(AppState state, Person person) =>
        state.Doc.Change(() => state.Doc.Data.HomePersonId = person.Id);
}
