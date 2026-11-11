using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;
using Mycelia.Model;

namespace Mycelia.Ai.Tools;

/// <summary>
/// The tools that change the tree (Ask by default, TDD 22.3).  Everything is looked up and checked before the change, so
/// a refused call leaves no empty undo step; then the change runs as one undo step (<see cref="Change"/>).
/// </summary>
public sealed partial class AssistantTools
{
    private static readonly string[] Relations = ["parent", "child", "spouse", "sibling"];

    /// <summary>
    /// Added to every notes parameter: a test with a real model (11 November 2026) wrote "please advise if want me to
    /// search..." into a person's notes.
    /// </summary>
    private const string NoteRule = " Notes are part of the tree, read by anyone who opens it later: what is known and where it came from, never questions, suggestions or messages to the person (say those in your reply).";

    private IEnumerable<AssistantTool> ChangeTools() =>
    [
        Write("add_person", "Add a person",
            "Adds a person, optionally with birth and death, and optionally linked to someone already in the tree as their parent, child, spouse or sibling. Gives the new person's id.",
            Schema.Object(
                ("given_names", Schema.String("Given names."), false),
                ("surname", Schema.String("Surname (at birth, for a woman)."), false),
                ("suffix", Schema.String("Suffix, such as Jr."), false),
                ("sex", Schema.String("Male, Female or Unknown.", "Male", "Female", "Unknown"), false),
                ("birth_date", Schema.String("Birth date as it should be recorded (\"12 Mar 1890\", \"Abt. 1850\")."), false),
                ("birth_place", Schema.String("Birth place, most local part first (\"Leeds, Yorkshire, England\")."), false),
                ("death_date", Schema.String("Death date."), false),
                ("death_place", Schema.String("Death place."), false),
                ("relation", Schema.String("How the new person is related to relative_id: parent (the new person is their parent), child, spouse or sibling.", Relations), false),
                ("relative_id", Schema.String("The person already in the tree the new person is related to."), false),
                ("family_id", Schema.String("For a child: which of the relative's families (couples) the child belongs to; the first if left out."), false),
                ("another_partner", Schema.Boolean(AnotherPartnerHelp), false)),
            AddPerson,
            args => $"Add {Quoted(PersonNameFrom(args))}" + (args.Str("relation") is { } r && args.Str("relative_id") is not null ? $" as the {r} of {NameOf(args, "relative_id")}." : " to the tree.")
                    + (args.Str("relation") == "spouse" ? UnnamedPartnerNote(args, Doc.GetPerson(args.Str("relative_id"))) : "")),

        Write("update_person", "Change a person",
            "Changes a person's names, sex, notes, Private mark or preferred parents. Only the fields given change.",
            Schema.Object(
                ("person_id", Schema.String("Whose details."), true),
                ("given_names", Schema.String("New given names."), false),
                ("surname", Schema.String("New surname."), false),
                ("suffix", Schema.String("New suffix (an empty string removes it)."), false),
                ("sex", Schema.String("Male, Female or Unknown.", "Male", "Female", "Unknown"), false),
                ("notes", Schema.String("The whole new notes text (replaces the notes)." + NoteRule), false),
                ("private", Schema.Boolean("Mark the person Private (left out of exports unless chosen)."), false),
                ("preferred_parents", Schema.String("For someone with two or more sets of parents: the family_id (from get_person's parents) the pedigree and charts should follow."), false)),
            UpdatePerson,
            args =>
            {
                var parts = new[] { "given_names", "surname", "suffix", "sex", "notes", "private" }.Where(k => args[k] is not null).Select(k => k.Replace('_', ' ')).ToList();
                if (Doc.GetFamily(args.Str("preferred_parents")) is { } pf) parts.Add($"preferred parents {Doc.CoupleName(pf)}");
                return $"Change {NameOf(args)}: {string.Join(", ", parts)}.";
            }),

        Write("delete_person", "Delete a person",
            "Deletes a person and all of their facts. Their relatives stay in the tree.",
            Schema.Object(("person_id", Schema.String("Who to delete."), true)),
            args =>
            {
                var p = PersonArg(args);
                var name = p.DisplayName;
                return Change(() => { Doc.RemovePerson(p); return $"Deleted {name}."; });
            },
            args => $"Delete {NameOf(args)} and all of their facts."),

        Write("merge_people", "Merge two people",
            "Merges two records of the same person into one: the second's facts, relationships and media move to the first, and the second is removed.",
            Schema.Object(
                ("keep_id", Schema.String("The record to keep."), true),
                ("merge_id", Schema.String("The record merged into it and removed."), true)),
            args =>
            {
                var keep = PersonArg(args, "keep_id");
                var merge = PersonArg(args, "merge_id");
                if (keep == merge) throw new ArgumentException("keep_id and merge_id are the same person.");
                var name = merge.DisplayName;
                return Change(() => { PersonMerge.Merge(Doc, keep, merge); return $"Merged {name} into {keep.DisplayName} ({keep.Id})."; });
            },
            args => $"Merge {NameOf(args, "merge_id")} into {NameOf(args, "keep_id")}; the second record is removed."),

        Write("set_home_person", "Set the home person",
            "Makes someone the home person, from whom relationships are described.",
            Schema.Object(("person_id", Schema.String("The new home person."), true)),
            args =>
            {
                var p = PersonArg(args);
                return Change(() => { Doc.Data.HomePersonId = p.Id; return $"{p.DisplayName} is now the home person."; });
            },
            args => $"Make {NameOf(args)} the home person."),

        Write("set_bookmark", "Bookmark a person",
            "Adds or removes a bookmark for a person (bookmarks are not undo steps).",
            Schema.Object(
                ("person_id", Schema.String("Who."), true),
                ("bookmarked", Schema.Boolean("true to bookmark, false to remove the bookmark."), true)),
            args =>
            {
                var p = PersonArg(args);
                var want = args.Bool("bookmarked", true);
                if (_state.IsBookmarked(p) != want) _state.ToggleBookmark(p);
                return want ? $"{p.DisplayName} is bookmarked." : $"{p.DisplayName}'s bookmark is removed.";
            },
            args => args.Bool("bookmarked", true) ? $"Bookmark {NameOf(args)}." : $"Remove {NameOf(args)}'s bookmark."),

        Write("add_fact", "Add a fact",
            "Adds a fact to a person (birth, baptism, occupation, residence, census...) or to a family (marriage, divorce...). Dates are kept exactly as given. Gives the fact's id, for add_citation.",
            Schema.Object(
                ("owner_id", Schema.String("The person's id or the family's id."), true),
                ("type", Schema.String("The fact type's name (list_fact_types), such as Birth, Baptism, Occupation, Residence, Census, Marriage."), true),
                ("date", Schema.String("The date as it should be recorded (\"12 Mar 1890\", \"Abt. 1850\", \"Bet. 1850 and 1855\")."), false),
                ("place", Schema.String("The place, most local part first; an existing place with the same name is used."), false),
                ("description", Schema.String("The description (an occupation's name, a residence's address...)."), false),
                ("note", Schema.String("A note on the fact." + NoteRule), false),
                ("private", Schema.Boolean("Mark the fact Private."), false),
                ("preferred", Schema.Boolean("Make it the preferred fact of its type (the first of a type always is)."), false)),
            AddFact,
            args => $"Add {Article(args.Str("type"))} fact to {OwnerNameOf(args.Str("owner_id"))}{Detail(args)}."),

        Write("update_fact", "Change a fact",
            "Changes a fact's type, date, place, description, note, Private mark or whether it is preferred. Only the fields given change; an empty string clears a field.",
            Schema.Object(
                ("fact_id", Schema.String("The fact's id."), true),
                ("type", Schema.String("A new fact type."), false),
                ("date", Schema.String("The new date as it should be recorded."), false),
                ("place", Schema.String("The new place."), false),
                ("description", Schema.String("The new description."), false),
                ("note", Schema.String("The new note." + NoteRule), false),
                ("private", Schema.Boolean("Private or not."), false),
                ("preferred", Schema.Boolean("Make it the preferred fact of its type."), false)),
            UpdateFact,
            args => $"Change {FactLabel(args.Str("fact_id"))}{Detail(args, "type", "date", "place", "description", "note", "private")}."),

        Write("delete_fact", "Delete a fact",
            "Deletes a fact and its citations.",
            Schema.Object(("fact_id", Schema.String("The fact's id."), true)),
            args =>
            {
                var (fact, list, owner) = FactArg(args);
                var label = $"{fact.Type.Name} of {Doc.OwnerName(owner)}";
                return Change(() =>
                {
                    list.Remove(fact);
                    if (fact.Preferred && list.FirstOrDefault(f => f.TypeId == fact.TypeId) is { } next) next.Preferred = true;
                    if (owner is Person p) p.RaiseFactsChanged();
                    return $"Deleted the {label}.";
                });
            },
            args => $"Delete {FactLabel(args.Str("fact_id"))}."),

        Write("add_citation", "Cite a source",
            "Cites a source for a fact: where in the source (page, entry, reference), the record's own web address and when it was looked at, and a transcription or extract of what it says. Name an existing source by id, or give a title: a source with that exact title is used, or a new one is made. Cite the fact for the event the record is of: a baptism entry goes on a Baptism fact, not on the Birth.",
            Schema.Object(
                ("fact_id", Schema.String("The fact the source supports: the one for the event the record is of."), true),
                ("source_id", Schema.String("An existing source's id."), false),
                ("source_title", Schema.String("Or a source title (found or created)."), false),
                ("source_url", Schema.String("For a new source: its web address (the database or catalogue)."), false),
                ("detail", Schema.String("Where in the source: page, folio, entry or reference."), false),
                ("url", Schema.String("The record's own web address."), false),
                ("accessed", Schema.String("The date the record was looked at, such as 11 Nov 2026."), false),
                ("text", Schema.String("A transcription or extract of what the record itself says, with no label; never the citation."), false)),
            AddCitation,
            args =>
            {
                var source = Doc.GetSource(args.Str("source_id"))?.ToString() ?? Quoted(args.Str("source_title"));
                var detail = args.Str("detail") is { } d ? $" ({d})" : "";
                return $"Cite {(source.Length > 0 ? source : "a source")} for {FactLabel(args.Str("fact_id"))}{detail}.";
            }),

        Write("remove_citation", "Remove a citation",
            "Removes a source's citation from a fact (the source stays).",
            Schema.Object(
                ("fact_id", Schema.String("The fact."), true),
                ("source_id", Schema.String("The cited source."), true)),
            args =>
            {
                var (fact, _, _) = FactArg(args);
                var source = SourceArg(args);
                if (!fact.Citations.Any(c => c.SourceId == source.Id)) throw new ArgumentException("That fact doesn't cite that source.");
                return Change(() => { fact.Citations.RemoveAll(c => c.SourceId == source.Id); return $"Removed the citation of {source} from the {fact.Type.Name}."; });
            },
            args => $"Remove the citation of {Doc.GetSource(args.Str("source_id"))?.ToString() ?? "a source"} from {FactLabel(args.Str("fact_id"))}."),

        Write("add_source", "Add a source",
            "Adds a source (a register, census, certificate, book, website, database). Gives its id.",
            SourceSchema(withId: false),
            args =>
            {
                var title = args.Str("title") ?? throw new ArgumentException("title is required.");
                // Checks a repository id before the change; a new repository is only made inside it.
                RepositoryFor(args, create: false);
                Source? made = null;
                Change(() =>
                {
                    made = new Source { Title = title };
                    FillSource(made, args, RepositoryFor(args, create: true));
                    Doc.Data.Sources.Add(made);
                    return "";
                });
                return $"Added the source \"{made!}\" ({made!.Id}).";
            },
            args => $"Add the source {Quoted(args.Str("title"))}."),

        Write("update_source", "Change a source",
            "Changes a source's title, author, publication details, call number, repository or comments. Only the fields given change.",
            SourceSchema(withId: true),
            args =>
            {
                var s = SourceArg(args);
                RepositoryFor(args, create: false);
                return Change(() => { FillSource(s, args, RepositoryFor(args, create: true)); return $"Changed the source \"{s}\"."; });
            },
            args => $"Change the source {Doc.GetSource(args.Str("source_id"))?.ToString() ?? ""}."),

        Write("add_repository", "Add a repository",
            "Adds a repository (an archive, library or website holding sources). Gives its id.",
            Schema.Object(
                ("name", Schema.String("Name."), true),
                ("address", Schema.String("Address."), false),
                ("phone", Schema.String("Phone."), false),
                ("email", Schema.String("Email."), false)),
            args =>
            {
                var name = args.Str("name") ?? throw new ArgumentException("name is required.");
                Repository? r = null;
                Change(() =>
                {
                    r = new Repository { Name = name, Address = args.Str("address") ?? "", Phone = args.Str("phone") ?? "", Email = args.Str("email") ?? "" };
                    Doc.Data.Repositories.Add(r);
                    return "";
                });
                return $"Added the repository \"{name}\" ({r!.Id}).";
            },
            args => $"Add the repository {Quoted(args.Str("name"))}."),

        Write("update_place", "Change a place",
            "Changes a place's full name (most local part first), latitude, longitude or notes. Every fact at the place follows.",
            Schema.Object(
                ("place_id", Schema.String("The place."), true),
                ("name", Schema.String("The new full name."), false),
                ("latitude", Schema.String("Latitude in decimal degrees (51.4545)."), false),
                ("longitude", Schema.String("Longitude in decimal degrees (-2.5879)."), false),
                ("notes", Schema.String("The whole new notes text." + NoteRule), false)),
            args =>
            {
                var place = PlaceArg(args);
                if (args.Str("name") is { } n && Doc.Data.Places.Any(p => p != place && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase)))
                    throw new ArgumentException($"Another place is already called \"{n}\".  combine_places merges two places.");
                foreach (var key in new[] { "latitude", "longitude" })
                    if (args.Str(key) is { } v && !double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                        throw new ArgumentException($"{key} must be a number in decimal degrees, such as 51.4545.");
                return Change(() =>
                {
                    if (args.Str("name") is { } name) place.Name = name;
                    if (args["latitude"] is not null) place.Latitude = args.Str("latitude") ?? "";
                    if (args["longitude"] is not null) place.Longitude = args.Str("longitude") ?? "";
                    if (args["notes"] is not null) place.Notes = args.Str("notes") ?? "";
                    return $"Changed the place \"{place.Name}\".";
                });
            },
            args => $"Change the place {Doc.GetPlace(args.Str("place_id"))?.Name ?? ""}{Detail(args, "name", "latitude", "longitude")}."),

        Write("combine_places", "Combine two places",
            "Moves every fact at one place to another and removes the first (its coordinates are kept when the other has none).",
            Schema.Object(
                ("place_id", Schema.String("The place to remove."), true),
                ("into_place_id", Schema.String("The place to keep."), true)),
            args =>
            {
                var from = PlaceArg(args);
                var into = PlaceArg(args, "into_place_id");
                if (from == into) throw new ArgumentException("The two places are the same.");
                return Change(() =>
                {
                    var moved = 0;
                    foreach (var (_, f) in Doc.AllFacts().Where(x => x.Fact.PlaceId == from.Id)) { f.PlaceId = into.Id; moved++; }
                    if (into.Latitude.Length == 0) { into.Latitude = from.Latitude; into.Longitude = from.Longitude; }
                    Doc.Data.Places.Remove(from);
                    return $"Moved {moved} facts from \"{from.Name}\" to \"{into.Name}\" and removed \"{from.Name}\".";
                });
            },
            args => $"Combine {Doc.GetPlace(args.Str("place_id"))?.Name ?? "a place"} into {Doc.GetPlace(args.Str("into_place_id"))?.Name ?? "another"}."),

        Write("link_parent", "Link a parent",
            "Makes someone already in the tree a parent of a person.",
            Schema.Object(
                ("child_id", Schema.String("The child."), true),
                ("parent_id", Schema.String("The parent."), true),
                ("relationship", Schema.String("How the child is related to this parent (default Natural).", EnumNames<ChildNature>()), false)),
            args =>
            {
                var child = PersonArg(args, "child_id");
                var parent = PersonArg(args, "parent_id");
                if (child == parent) throw new ArgumentException("A person can't be their own parent.");
                var nature = EnumArg(args, "relationship", ChildNature.Natural);
                return Change(() =>
                {
                    Doc.SetParent(child, parent);
                    SetNature(child, parent, nature);
                    return $"{parent.DisplayName} is now a parent of {child.DisplayName}.";
                });
            },
            args => $"Make {NameOf(args, "parent_id")} a parent of {NameOf(args, "child_id")}."),

        Write("link_spouse", "Link a spouse",
            "Makes two people already in the tree a couple. When the first has a family with the partner missing, the second fills it, so its children gain them as a parent; otherwise a new family is made. Gives the family's id, for add_fact (marriage) and link_child.",
            Schema.Object(
                ("person_id", Schema.String("One partner."), true),
                ("spouse_id", Schema.String("The other partner."), true),
                ("couple_type", Schema.String("Spouse (default), Partner, Friend, Single, Other or Unknown.", EnumNames<CoupleType>()), false),
                ("another_partner", Schema.Boolean(AnotherPartnerHelp), false)),
            args =>
            {
                var a = PersonArg(args);
                var b = PersonArg(args, "spouse_id");
                if (a == b) throw new ArgumentException("A person can't be their own spouse.");
                if (Doc.SpouseFamilies(a).FirstOrDefault(f => f.HasPartner(b.Id)) is { } existing)
                    throw new ArgumentException($"They are already a couple (family {existing.Id}).");
                if (!args.Bool("another_partner", false))
                {
                    CheckNoUnnamedPartner(a, b);
                    CheckNoUnnamedPartner(b, a);
                }
                var type = EnumArg(args, "couple_type", CoupleType.Spouse);
                Family? made = null;
                var before = Doc.Data.Families.Count;
                Change(() =>
                {
                    made = Doc.LinkCouple(a, b);
                    // A family filled keeps the type it had unless one is given.
                    if (Doc.Data.Families.Count > before || args["couple_type"] is not null) made.Type = type;
                    return "";
                });
                var kids = Doc.Data.Families.Count > before || made!.Children.Count == 0 ? "" : $", which already had {made.Children.Count} {(made.Children.Count == 1 ? "child" : "children")}";
                return $"{a.DisplayName} and {b.DisplayName} are now a couple (family {made!.Id}{kids}).";
            },
            args => $"Make {NameOf(args)} and {NameOf(args, "spouse_id")} a couple."
                    + UnnamedPartnerNote(args, Doc.GetPerson(args.Str("person_id")), Doc.GetPerson(args.Str("spouse_id")))
                    + UnnamedPartnerNote(args, Doc.GetPerson(args.Str("spouse_id")), Doc.GetPerson(args.Str("person_id")))),

        Write("link_child", "Link a child",
            "Makes someone already in the tree a child of a person, in one of their families (couples); the first family, or a new single-parent family, when none is named.",
            Schema.Object(
                ("parent_id", Schema.String("The parent."), true),
                ("child_id", Schema.String("The child."), true),
                ("family_id", Schema.String("Which of the parent's families."), false),
                ("relationship", Schema.String("How the child is related to the parents (default Natural).", EnumNames<ChildNature>()), false)),
            args =>
            {
                var parent = PersonArg(args, "parent_id");
                var child = PersonArg(args, "child_id");
                if (child == parent) throw new ArgumentException("A person can't be their own child.");
                var family = args.Str("family_id") is null ? null : FamilyArg(args);
                if (family is not null && !family.HasPartner(parent.Id)) throw new ArgumentException("That family isn't one of the parent's.");
                var nature = EnumArg(args, "relationship", ChildNature.Natural);
                return Change(() =>
                {
                    var fam = family ?? Doc.SpouseFamilies(parent).FirstOrDefault() ?? Doc.AddFamily(parent, null);
                    Doc.AddChild(fam, child, nature);
                    return $"{child.DisplayName} is now a child of {Doc.CoupleName(fam)} (family {fam.Id}).";
                });
            },
            args => $"Make {NameOf(args, "child_id")} a child of {NameOf(args, "parent_id")}." + OtherParentsNote(args)),

        Write("unlink", "Unlink from a family",
            "Takes a person out of a family: a child from their parents, or a partner from a couple. Nobody is deleted.",
            Schema.Object(
                ("family_id", Schema.String("The family."), true),
                ("person_id", Schema.String("The child or partner to take out."), true)),
            args =>
            {
                var f = FamilyArg(args);
                var p = PersonArg(args);
                var couple = Doc.CoupleName(f);
                if (f.Children.Any(c => c.PersonId == p.Id))
                    return Change(() => { Doc.DetachChild(f, p); return $"{p.DisplayName} is no longer a child of {couple}."; });
                if (f.HasPartner(p.Id))
                    return Change(() =>
                    {
                        // DetachPartner removes the family when it is left empty.
                        Doc.DetachPartner(f, p);
                        return $"{p.DisplayName} is no longer a partner in {couple}.";
                    });
                throw new ArgumentException($"{p.DisplayName} isn't in that family.");
            },
            args => $"Take {NameOf(args)} out of the family of {CoupleOf(args)}."),

        Write("combine_families", "Combine two families",
            "Combines two families of the same person into one: a family with the spouse unknown into the one with the spouse (the spouse becomes the parent of its children), or the same couple recorded twice. The family with the spouse is kept; children, couple facts and notes move across. Two families with different named spouses can't be combined: if those spouses are the same person, use merge_people.",
            Schema.Object(
                ("family_id", Schema.String("One family."), true),
                ("other_family_id", Schema.String("The other family of the same person."), true)),
            args =>
            {
                var first = FamilyArg(args);
                var second = Doc.GetFamily(args.Str("other_family_id")) ?? throw new ArgumentException("There is no family with that other_family_id.  get_person lists a person's families.");
                if (PersonMerge.SharedPartner(Doc, first, second) is null)
                    throw new ArgumentException("Those two families don't share a person with at most one spouse between them, so they can't be combined.  If their spouses are the same person, merge_people them first.");
                var ids = (first.Id, second.Id);
                Family? kept = null;
                var moved = 0;
                Change(() => { (kept, moved) = PersonMerge.CombineFamilies(Doc, first, second); return ""; });
                // Ids and counts only: names go to the model only through Ref and the withholding checks (TDD 22.4).
                return $"Combined families {ids.Item1} and {ids.Item2} into family {kept!.Id}, with {moved} {(moved == 1 ? "child" : "children")} moved across; get_family shows it.";
            },
            args =>
            {
                var a = Doc.GetFamily(args.Str("family_id"));
                var b = Doc.GetFamily(args.Str("other_family_id"));
                if (a is null || b is null || PersonMerge.SharedPartner(Doc, a, b) is not { } shared) return "Combine two families.";
                var keep = PersonMerge.KeptFamily(Doc, a, b, shared);
                var other = keep == a ? b : a;
                return $"Combine the family of {Doc.CoupleName(other)} ({other.Children.Count} {(other.Children.Count == 1 ? "child" : "children")}) into the family of {Doc.CoupleName(keep)}; the first family is removed.";
            }),

        Write("update_family", "Change a family",
            "Changes a couple's type and status, or how one child is related to each partner.",
            Schema.Object(
                ("family_id", Schema.String("The family."), true),
                ("couple_type", Schema.String("The couple's type.", EnumNames<CoupleType>()), false),
                ("status", Schema.String("The couple's status.", EnumNames<CoupleStatus>()), false),
                ("child_id", Schema.String("A child whose relationship to the partners changes."), false),
                ("relationship_to_partner1", Schema.String("The child's relationship to partner 1.", EnumNames<ChildNature>()), false),
                ("relationship_to_partner2", Schema.String("The child's relationship to partner 2.", EnumNames<ChildNature>()), false)),
            args =>
            {
                var f = FamilyArg(args);
                var type = EnumArg(args, "couple_type", f.Type);
                var status = EnumArg(args, "status", f.Status);
                ChildLink? link = null;
                if (args.Str("child_id") is { } childId)
                    link = f.Children.FirstOrDefault(c => c.PersonId == childId) ?? throw new ArgumentException("That person isn't a child of this family.");
                var n1 = link is null ? ChildNature.Natural : EnumArg(args, "relationship_to_partner1", link.Nature1);
                var n2 = link is null ? ChildNature.Natural : EnumArg(args, "relationship_to_partner2", link.Nature2);
                return Change(() =>
                {
                    f.Type = type;
                    f.Status = status;
                    if (link is not null) { link.Nature1 = n1; link.Nature2 = n2; }
                    return $"Changed the family of {Doc.CoupleName(f)}.";
                });
            },
            args => $"Change the family of {CoupleOf(args)}{Detail(args, "couple_type", "status", "relationship_to_partner1", "relationship_to_partner2")}."),

        Write("add_media", "Add a media item",
            "Adds a picture or document file already on this computer as a media item, optionally linked to people and as someone's portrait. The file stays where it is. Gives the item's id.",
            Schema.Object(
                ("file", Schema.String("The file's full path."), true),
                ("caption", Schema.String("Caption (the file name if left out)."), false),
                ("date", Schema.String("Date of the picture or document."), false),
                ("category", Schema.String("Category (Photo, Document, Certificate...)."), false),
                ("description", Schema.String("Description."), false),
                ("person_ids", Schema.Array(Schema.String("A person's id."), "People in the item."), false),
                ("portrait_of", Schema.String("Make it this person's portrait."), false)),
            AddMedia,
            args => $"Add the file {Path.GetFileName(args.Str("file") ?? "")} as a media item" + (args.Strings("person_ids").Count > 0 ? $" linked to {string.Join(", ", args.Strings("person_ids").Select(id => Doc.GetPerson(id)?.DisplayName ?? id))}." : ".")),

        Write("link_media", "Link a media item",
            "Links a media item to a person, optionally as their portrait.",
            Schema.Object(
                ("media_id", Schema.String("The media item."), true),
                ("person_id", Schema.String("The person."), true),
                ("portrait", Schema.Boolean("Make it the person's portrait."), false)),
            args =>
            {
                var m = Doc.GetMedia(args.Str("media_id")) ?? throw new ArgumentException("There is no media item with that id.  Use list_media.");
                var p = PersonArg(args);
                var portrait = args.Bool("portrait", false);
                return Change(() =>
                {
                    if (!m.PersonIds.Contains(p.Id)) m.PersonIds.Add(p.Id);
                    if (portrait) p.PhotoMediaId = m.Id;
                    return $"Linked \"{m.Caption}\" to {p.DisplayName}{(portrait ? " as their portrait" : "")}.";
                });
            },
            args => $"Link the media item {Doc.GetMedia(args.Str("media_id"))?.Caption ?? ""} to {NameOf(args)}{(args.Bool("portrait", false) ? " as their portrait" : "")}."),

        Write("add_fact_type", "Add a fact type",
            "Adds a fact type of this tree's own, for a person or a couple, when none of the built-in ones fits (it is written to GEDCOM as a named event).",
            Schema.Object(
                ("name", Schema.String("The type's name."), true),
                ("for", Schema.String("person or couple (default person).", "person", "couple"), false),
                ("takes_date", Schema.Boolean("Takes a date (default true)."), false),
                ("takes_place", Schema.Boolean("Takes a place (default true)."), false)),
            args =>
            {
                var name = args.Str("name") ?? throw new ArgumentException("name is required.");
                var scope = args.Str("for") == "couple" ? FactScope.Couple : FactScope.Person;
                if ((scope == FactScope.Person ? FactTypes.PersonTypes : FactTypes.CoupleTypes).Any(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                    throw new ArgumentException($"There is already a fact type called \"{name}\".");
                return Change(() =>
                {
                    Doc.Data.CustomFactTypes.Add(new CustomFactType
                    {
                        Id = TreeImport.NextCustomId(Doc.Data), Name = name, Scope = scope,
                        HasDate = args.Bool("takes_date", true), HasPlace = args.Bool("takes_place", true),
                    });
                    return $"Added the {(scope == FactScope.Person ? "person" : "couple")} fact type \"{name}\".";
                });
            },
            args => $"Add a fact type called {Quoted(args.Str("name"))}."),

        Write("add_historical_event", "Add a historical event",
            "Adds a historical event to the tree's timelines (a war, an epidemic, a local disaster, a change in the law).",
            Schema.Object(
                ("title", Schema.String("Title."), true),
                ("date", Schema.String("Date or start date."), true),
                ("end_date", Schema.String("End date, when it lasted a while."), false),
                ("region", Schema.String("Where it applies (\"United Kingdom\", \"World\")."), false),
                ("category", Schema.String("Category (War, Health, Law...)."), false),
                ("description", Schema.String("Description."), false)),
            args =>
            {
                var title = args.Str("title") ?? throw new ArgumentException("title is required.");
                var date = args.Str("date") ?? throw new ArgumentException("date is required.");
                return Change(() =>
                {
                    Doc.Data.HistoricalEvents.Add(new HistoricalEvent
                    {
                        Title = title, Date = date, EndDate = args.Str("end_date") ?? "", Region = args.Str("region") ?? "",
                        Category = args.Str("category") ?? "", Description = args.Str("description") ?? "",
                    });
                    return $"Added the historical event \"{title}\" ({date}).";
                });
            },
            args => $"Add the historical event {Quoted(args.Str("title"))} ({args.Str("date")})."),

        Write("replace_text", "Replace text",
            "Replaces text in every field that holds it (the fields find_text lists), as one undo step.",
            Schema.Object(
                ("text", Schema.String("The text to replace."), true),
                ("replacement", Schema.String("What to put instead."), true),
                ("match_case", Schema.Boolean("Match capitals exactly (default false)."), false),
                ("whole_word", Schema.Boolean("Whole words only (default false)."), false)),
            args =>
            {
                var text = args.Str("text") ?? throw new ArgumentException("text is required.");
                var replacement = args["replacement"]?.ToString() ?? throw new ArgumentException("replacement is required.");
                bool matchCase = args.Bool("match_case", false), whole = args.Bool("whole_word", false);
                var hits = TreeTools.Find(Doc, text, matchCase, whole);
                if (hits.Count == 0) return $"Nothing holds \"{text}\"; nothing was changed.";
                return Change(() => $"Replaced \"{text}\" with \"{replacement}\" in {TreeTools.Replace(hits, text, replacement, matchCase, whole)} fields.");
            },
            args => $"Replace {Quoted(args.Str("text"))} with {Quoted(args["replacement"]?.ToString())} everywhere in the tree ({TreeTools.Find(Doc, args.Str("text") ?? "", args.Bool("match_case", false), args.Bool("whole_word", false)).Count} fields)."),

        Write("sort_children", "Sort children by birth",
            "Puts every family's children in birth order (children with no birth date go last).",
            Schema.Object(),
            _ => Change(() => TreeTools.SortAllChildrenByBirth(Doc) is var n && n == 0 ? "Every family was already in birth order." : $"Reordered the children of {n} families."),
            _ => "Put every family's children in birth order."),

        Write("convert_names", "Convert names",
            "Converts names written all in capitals or all in small letters to mixed case (Mc, Mac and O' names, hyphens), optionally with surnames in capitals.",
            Schema.Object(("surnames_in_capitals", Schema.Boolean("Write surnames in capitals (default false)."), false)),
            args => Change(() => TreeTools.ConvertNames(Doc, args.Bool("surnames_in_capitals", false)) is var n && n == 0 ? "No names needed changing." : $"Changed {n} names."),
            args => args.Bool("surnames_in_capitals", false) ? "Convert names to mixed case, with surnames in capitals." : "Convert names to mixed case."),

        Write("undo", "Undo",
            "Takes back the last change to the tree, whoever made it (the same as the Undo button).",
            Schema.Object(),
            _ =>
            {
                if (!Doc.CanUndo) return "There is nothing to undo.";
                Doc.Undo();
                ForgetLiving();
                return "Took back the last change.";
            },
            _ => "Undo the last change to the tree."),
    ];

    // ---- add_person ----

    private string PersonNameFrom(JsonObject args) =>
        string.Join(' ', new[] { args.Str("given_names"), args.Str("surname"), args.Str("suffix") }.Where(s => s is not null));

    private string AddPerson(JsonObject args)
    {
        var given = args.Str("given_names") ?? "";
        var surname = args.Str("surname") ?? "";
        if (given.Length == 0 && surname.Length == 0) throw new ArgumentException("Give at least given_names or surname.");
        var sex = EnumArg(args, "sex", Sex.Unknown);
        var relation = args.Str("relation");
        Person? relative = null;
        Family? family = null;
        if (relation is not null)
        {
            if (!Relations.Contains(relation)) throw new ArgumentException("relation must be parent, child, spouse or sibling.");
            relative = PersonArg(args, "relative_id");
            if (relation == "spouse" && !args.Bool("another_partner", false)) CheckNoUnnamedPartner(relative);
            if (args.Str("family_id") is not null)
            {
                family = FamilyArg(args);
                if (!family.HasPartner(relative.Id)) throw new ArgumentException("That family isn't one of the relative's.");
            }
        }

        Person? made = null;
        Family? into = null;
        Change(() =>
        {
            var p = Doc.AddPerson(given, surname, sex);
            p.Suffix = args.Str("suffix") ?? "";
            if (args.Str("birth_date") is not null || args.Str("birth_place") is not null)
                Doc.AddFact(p.Facts, FactTypes.Birth, args.Str("birth_date") ?? "", args.Str("birth_place") ?? "");
            if (args.Str("death_date") is not null || args.Str("death_place") is not null)
                Doc.AddFact(p.Facts, FactTypes.Death, args.Str("death_date") ?? "", args.Str("death_place") ?? "");
            switch (relation)
            {
                case "parent":
                    Doc.SetParent(relative!, p);
                    into = Doc.ParentFamilies(relative!).FirstOrDefault(f => f.HasPartner(p.Id));
                    break;
                case "child":
                    into = family ?? Doc.SpouseFamilies(relative!).FirstOrDefault() ?? Doc.AddFamily(relative, null);
                    Doc.AddChild(into, p);
                    break;
                case "spouse":
                    // The same rule as Add spouse: a family with a partner missing is filled, so its children gain the new parent.
                    into = Doc.LinkCouple(relative!, p);
                    break;
                case "sibling":
                    into = Doc.EnsureParentFamily(relative!);
                    Doc.AddChild(into, p);
                    break;
            }
            p.RaiseFactsChanged();
            made = p;
            return "";
        });
        var text = $"Added {made!.DisplayName} ({made.Id})";
        if (relation is not null) text += $" as the {relation} of {relative!.DisplayName}" + (into is not null ? $" (family {into.Id})" : "");
        return text + ".";
    }

    /// <summary>
    /// The link_child card's note when the child already has parents in another family ("Arthur Ashworth is already a
    /// child of Thomas Ashworth &amp; Ellen Barraclough."), so linking the wrong person of the same name can be seen before it is approved.
    /// From a test with a real model (11 November 2026), which linked a namesake from another family as a man's son.
    /// </summary>
    private string OtherParentsNote(JsonObject args)
    {
        if (Doc.GetPerson(args.Str("child_id")) is not { } child || Doc.GetPerson(args.Str("parent_id")) is not { } parent) return "";
        var target = Doc.GetFamily(args.Str("family_id")) ?? Doc.SpouseFamilies(parent).FirstOrDefault();
        var others = Doc.ParentFamilies(child)
            .Where(f => f != target && (f.Partner1Id is not null || f.Partner2Id is not null))
            .Select(f => Doc.CoupleName(f)).ToList();
        return others.Count == 0 ? "" : $"  {child.DisplayName} is already a child of {string.Join(" and of ", others)}.";
    }

    // ---- partners with no name ----
    // From a test with a real model (11 November 2026): told the name of a man's wife, recorded as "?" and the mother
    // of his son, it added her as a second partner instead of naming the record, so the son kept "?" as his mother.

    private const string AnotherPartnerHelp = "true only when the new partner is someone other than a partner of theirs already in the tree with no name. Without it the call is refused when there is one: name that record with update_person instead.";

    /// <summary>The first partner of <paramref name="p"/> with no name, other than <paramref name="except"/>, and their family.</summary>
    private (Person Partner, Family Family)? UnnamedPartner(Person p, Person? except = null)
    {
        foreach (var f in Doc.SpouseFamilies(p))
            if (Doc.GetPerson(f.OtherPartner(p.Id)) is { } other && other != except && other.IsUnnamed)
                return (other, f);
        return null;
    }

    /// <summary>Refuses, before any change, a new partner for someone who already has a partner with no name.</summary>
    private void CheckNoUnnamedPartner(Person p, Person? except = null)
    {
        if (UnnamedPartner(p, except) is not { } found) return;
        var (partner, family) = found;
        var children = family.Children.Count switch { 0 => "", 1 => ", with 1 child", var n => $", with {n} children" };
        throw new ArgumentException(
            $"{p.DisplayName} already has a partner with no name ({partner.Id}, in family {family.Id}{children}).  If this is that partner, "
            + "name that record with update_person, or merge_people a record already added into it, so the family and its children keep them.  "
            + "If it is someone else, call again with another_partner: true.  Nothing was changed.");
    }

    /// <summary>
    /// The approval card's warning when the model says the new partner is someone else (another_partner); without it the
    /// call is refused anyway, so the card says nothing.
    /// </summary>
    private string UnnamedPartnerNote(JsonObject args, Person? p, Person? except = null) =>
        args.Bool("another_partner", false) && p is not null && UnnamedPartner(p, except) is not null
            ? $"  {p.DisplayName} already has a partner with no name." : "";

    private string UpdatePerson(JsonObject args)
    {
        var p = PersonArg(args);
        var sex = args.Str("sex") is null ? p.Sex : EnumArg(args, "sex", p.Sex);
        var preferred = args.Str("preferred_parents");
        if (preferred is not null && !Doc.ParentFamilies(p).Any(f => f.Id == preferred))
            throw new ArgumentException($"{preferred} isn't one of {p.DisplayName}'s parent families; get_person lists them.");
        return Change(() =>
        {
            if (preferred is not null) p.PreferredParentsId = preferred;
            if (args["given_names"] is not null) p.Given = args.Str("given_names") ?? "";
            if (args["surname"] is not null) p.Surname = args.Str("surname") ?? "";
            if (args["suffix"] is not null) p.Suffix = args.Str("suffix") ?? "";
            if (args["notes"] is not null) p.Notes = args["notes"]?.ToString() ?? "";
            if (args["private"] is not null) p.Private = args.Bool("private", p.Private);
            p.Sex = sex;
            return $"Changed {p.DisplayName} ({p.Id}).";
        });
    }

    // ---- facts and citations ----

    /// <summary>The person or family with this id, the facts list and the scope of its facts.</summary>
    private (object Owner, List<Fact> Facts, FactScope Scope) OwnerArg(string? id)
    {
        if (id is null) throw new ArgumentException("owner_id is required (a person's or a family's id).");
        if (Doc.GetPerson(id) is { } p) return (p, p.Facts, FactScope.Person);
        if (Doc.GetFamily(id) is { } f) return (f, f.Facts, FactScope.Couple);
        throw new ArgumentException($"There is no person or family with id {id}.");
    }

    private string OwnerNameOf(string? id) =>
        Doc.GetPerson(id)?.DisplayName ?? (Doc.GetFamily(id) is { } f ? Doc.CoupleName(f) : id ?? "someone");

    /// <summary>
    /// A fact for an approval card, with its date as typed ("the Birth (Abt. 1801) of Hannah Sutcliffe"), so a citation put on
    /// the wrong fact can be seen before it is approved.
    /// </summary>
    private string FactLabel(string? factId)
    {
        foreach (var (owner, f) in Doc.AllFacts())
            if (f.Id == factId) return $"the {f.Type.Name}{(f.Date.Trim().Length > 0 ? $" ({f.Date.Trim()})" : "")} of {Doc.OwnerName(owner)}";
        return "a fact";
    }

    private static string Article(string? type) =>
        string.IsNullOrWhiteSpace(type) ? "a" : ("AEIOU".Contains(char.ToUpperInvariant(type.Trim()[0])) ? "an " : "a ") + type.Trim();

    /// <summary>The fields an approval card lists: ": date 12 Mar 1890, place Leeds".</summary>
    private static string Detail(JsonObject args, params string[] keys)
    {
        if (keys.Length == 0) keys = ["date", "place", "description"];
        var parts = keys.Where(k => args[k] is not null).Select(k => $"{k.Replace('_', ' ')} {args[k]}").ToList();
        return parts.Count == 0 ? "" : ": " + string.Join(", ", parts);
    }

    private static void MakePreferred(List<Fact> facts, Fact fact)
    {
        foreach (var other in facts.Where(f => f.TypeId == fact.TypeId)) other.Preferred = other == fact;
    }

    private string AddFact(JsonObject args)
    {
        var (owner, facts, scope) = OwnerArg(args.Str("owner_id"));
        var type = FactTypeArg(args.Str("type"), scope);
        Fact? made = null;
        Change(() =>
        {
            made = Doc.AddFact(facts, type.Id, args.Str("date") ?? "", args.Str("place") ?? "", args.Str("description") ?? "");
            made.Note = args.Str("note") ?? "";
            made.Private = args.Bool("private", false);
            if (args.Bool("preferred", false)) MakePreferred(facts, made);
            if (owner is Person p) p.RaiseFactsChanged();
            return "";
        });
        var shown = made!.ParsedDate;
        var warning = made.Date.Length > 0 && !shown.IsValid ? $"  The program can't read \"{made.Date}\" as a date; it is kept as typed." : "";
        return $"Added the {type.Name} ({made.Id}) to {Doc.OwnerName(owner)}.{warning}";
    }

    private string UpdateFact(JsonObject args)
    {
        var (fact, list, owner) = FactArg(args);
        var type = args.Str("type") is { } t ? FactTypeArg(t, owner is Family ? FactScope.Couple : FactScope.Person) : fact.Type;
        return Change(() =>
        {
            fact.TypeId = type.Id;
            if (args["date"] is not null) fact.Date = args.Str("date") ?? "";
            if (args["place"] is not null) fact.PlaceId = Doc.GetOrCreatePlace(args.Str("place"))?.Id;
            if (args["description"] is not null) fact.Description = args.Str("description") ?? "";
            if (args["note"] is not null) fact.Note = args["note"]?.ToString() ?? "";
            if (args["private"] is not null) fact.Private = args.Bool("private", fact.Private);
            if (args.Bool("preferred", false)) MakePreferred(list, fact);
            if (owner is Person p) p.RaiseFactsChanged();
            return $"Changed the {fact.Type.Name} of {Doc.OwnerName(owner)}.";
        });
    }

    private string AddCitation(JsonObject args)
    {
        var (fact, _, owner) = FactArg(args);
        Source? source = args.Str("source_id") is not null ? SourceArg(args) : null;
        var title = args.Str("source_title");
        if (source is null && title is null) throw new ArgumentException("Give source_id or source_title.");
        source ??= Doc.Data.Sources.FirstOrDefault(s => string.Equals(s.Title, title, StringComparison.CurrentCultureIgnoreCase));
        var detail = args.Str("detail") ?? "";
        var url = args.Str("url") ?? "";
        foreach (var key in new[] { "url", "source_url" })
            if (args.Str(key) is { } address && !Ui.Dialogs.IsWebAddress(address))
                throw new ArgumentException($"{key} must be a web address starting with https:// or http://.");
        // The fact form shows accessed in the person's date style, so it must be a date the program reads.
        if (args.Str("accessed") is { } accessed && !GenDate.Parse(accessed).IsValid)
            throw new ArgumentException($"The program can't read \"{accessed}\" as a date.  Give accessed as a date such as 11 Nov 2026, or leave it out.");
        if (source is not null && fact.Citations.Any(c => c.SourceId == source.Id && c.Detail == detail && c.Url == url))
            return $"The {fact.Type.Name} already cites \"{source}\" with that detail; nothing was changed.";
        var created = source is null;
        return Change(() =>
        {
            if (source is null)
            {
                source = new Source { Title = title!, Url = args.Str("source_url") ?? "" };
                Doc.Data.Sources.Add(source);
            }
            fact.Citations.Add(new Citation { SourceId = source.Id, Detail = detail, Url = url, Accessed = args.Str("accessed") ?? "", Text = args["text"]?.ToString().Trim() ?? "" });
            return $"Cited \"{source}\" ({source.Id}{(created ? ", a new source" : "")}) for the {fact.Type.Name} of {Doc.OwnerName(owner)}.";
        });
    }

    private static JsonObject SourceSchema(bool withId) => Schema.Object(
        ("source_id", Schema.String("The source to change."), withId),
        ("title", Schema.String("Title."), !withId),
        ("author", Schema.String("Author or originator."), false),
        ("publisher", Schema.String("Publisher."), false),
        ("publisher_location", Schema.String("Where it was published."), false),
        ("publish_date", Schema.String("When it was published."), false),
        ("call_number", Schema.String("Call number or reference in the repository."), false),
        ("repository_id", Schema.String("The repository holding it."), false),
        ("repository_name", Schema.String("Or a repository's name (found or created)."), false),
        ("url", Schema.String("The source's web address (an online database, catalogue entry or digitised book)."), false),
        ("comments", Schema.String("Comments."), false));

    /// <summary>The repository named by id or name; with <paramref name="create"/> a new one is made for an unknown name.</summary>
    private Repository? RepositoryFor(JsonObject args, bool create)
    {
        if (args.Str("repository_id") is { } id)
            return Doc.GetRepository(id) ?? throw new ArgumentException("There is no repository with that id.  Use list_repositories.");
        if (args.Str("repository_name") is not { } name) return null;
        var found = Doc.Data.Repositories.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (found is not null || !create) return found;
        var made = new Repository { Name = name };
        Doc.Data.Repositories.Add(made);
        return made;
    }

    private static void FillSource(Source s, JsonObject args, Repository? repo)
    {
        if (args.Str("title") is { } t) s.Title = t;
        if (args["author"] is not null) s.Author = args.Str("author") ?? "";
        if (args["publisher"] is not null) s.Publisher = args.Str("publisher") ?? "";
        if (args["publisher_location"] is not null) s.PublisherLocation = args.Str("publisher_location") ?? "";
        if (args["publish_date"] is not null) s.PublishDate = args.Str("publish_date") ?? "";
        if (args["call_number"] is not null) s.CallNumber = args.Str("call_number") ?? "";
        if (args["comments"] is not null) s.Comments = args["comments"]?.ToString() ?? "";
        if (args["url"] is not null) s.Url = args.Str("url") ?? "";
        if (repo is not null) s.RepositoryId = repo.Id;
    }

    // ---- relationships and media ----

    /// <summary>How a child is related to one parent, in the slot that parent has in the child's family with them.</summary>
    private void SetNature(Person child, Person parent, ChildNature nature)
    {
        foreach (var f in Doc.ParentFamilies(child).Where(f => f.HasPartner(parent.Id)))
            foreach (var link in f.Children.Where(c => c.PersonId == child.Id))
            {
                if (f.Partner1Id == parent.Id) link.Nature1 = nature;
                else link.Nature2 = nature;
            }
    }

    private string AddMedia(JsonObject args)
    {
        var file = args.Str("file") ?? throw new ArgumentException("file is required.");
        if (!Path.IsPathRooted(file) || !File.Exists(file)) throw new ArgumentException($"There is no file at {file}.  Give the full path of a file on this computer.");
        if (Doc.Data.Media.FirstOrDefault(m => string.Equals(m.FilePath, file, StringComparison.OrdinalIgnoreCase)) is { } existing)
            throw new ArgumentException($"That file is already the media item \"{existing.Caption}\" ({existing.Id}); link_media links it to people.");
        var people = args.Strings("person_ids").Select(id => Doc.GetPerson(id) ?? throw new ArgumentException($"There is no person with id {id}.")).ToList();
        Person? portrait = args.Str("portrait_of") is not null ? PersonArg(args, "portrait_of") : null;
        MediaItem? made = null;
        Change(() =>
        {
            made = new MediaItem
            {
                FilePath = file, Caption = args.Str("caption") ?? Path.GetFileNameWithoutExtension(file), Date = args.Str("date") ?? "",
                Category = args.Str("category") ?? "", Description = args.Str("description") ?? "",
            };
            foreach (var p in people.Append(portrait).OfType<Person>().Distinct()) made.PersonIds.Add(p.Id);
            Doc.Data.Media.Add(made);
            if (portrait is not null) portrait.PhotoMediaId = made.Id;
            return "";
        });
        return $"Added the media item \"{made!.Caption}\" ({made.Id}).";
    }
}
