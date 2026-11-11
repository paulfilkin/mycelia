using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;
using Mycelia.Model;

namespace Mycelia.Ai.Tools;

/// <summary>The tools that read the tree (Allow by default, TDD 22.3).</summary>
public sealed partial class AssistantTools
{
    private const int DefaultLimit = 50, MaxLimit = 200;

    private IEnumerable<AssistantTool> ReadTools() =>
    [
        Read("get_context", "Get context",
            "Where the person is now: the open tree (name, file, counts, unsaved changes), the workspace on show, the selected person, the home person, and how living people are given to you.",
            Schema.Object(), _ => Context(), "Read the context"),

        Read("search_people", "Search people",
            "Finds people by name and, optionally, Soundex surname, sex, birth or death years, a place they have a fact at, and whether they are living. Gives ids for the other tools, a page at a time. Living people's details are withheld, so date and place filters don't match them.",
            Schema.Object(
                ("name", Schema.String("Part of a name (given names, surname or both, in either order)."), false),
                ("surname_soundex", Schema.String("A surname: matches surnames that sound alike (Soundex)."), false),
                ("sex", Schema.String("Male, Female or Unknown.", "Male", "Female", "Unknown"), false),
                ("born_from", Schema.Integer("Born in or after this year."), false),
                ("born_to", Schema.Integer("Born in or before this year."), false),
                ("died_from", Schema.Integer("Died in or after this year."), false),
                ("died_to", Schema.Integer("Died in or before this year."), false),
                ("place", Schema.String("Part of a place name the person has any fact at."), false),
                ("living", Schema.Boolean("true for living people only, false for people who have died."), false),
                ("offset", Schema.Integer("Skip this many matches (paging)."), false),
                ("limit", Schema.Integer($"Most to return (default {DefaultLimit}, at most {MaxLimit})."), false)),
            SearchPeople),

        Read("get_person", "Read a person",
            "Everything about one person: names, sex, notes, facts (with each date as typed and as the program reads it, place, description, sources and ids), parents and how each child is related, spouses with their families, marriages and children, media, the relationship to the home person, and data warnings. A living person's details are withheld as the context says.",
            Schema.Object(("person_id", Schema.String("The person's id."), true)), GetPerson),

        Read("get_family", "Read a family",
            "A couple or single parent: the partners, couple type and status, couple facts (marriage, divorce...) with ids, notes, and the children with how each is related to each partner.",
            Schema.Object(("family_id", Schema.String("The family's id (get_person lists them)."), true)), GetFamily),

        Read("get_ancestors", "Ancestors",
            "A person's ancestors, generation by generation, numbered as in an Ahnentafel (1 the person, 2 father, 3 mother, 4 father's father...), with birth and death.",
            Schema.Object(
                ("person_id", Schema.String("Whose ancestors."), true),
                ("generations", Schema.Integer("How many generations, counting the person (default 4, at most 7)."), false)),
            GetAncestors),

        Read("get_descendants", "Descendants",
            "A person's descendants, generation by generation, each with the family (couple) they belong to, and birth and death.",
            Schema.Object(
                ("person_id", Schema.String("Whose descendants."), true),
                ("generations", Schema.Integer("How many generations below the person (default 3, at most 5)."), false)),
            GetDescendants),

        Read("get_relationship", "Relationship",
            "How one person is related to another, in the program's wording (\"2nd cousin 1x removed\", \"father-in-law\"), and the path of people between them.",
            Schema.Object(
                ("person_id", Schema.String("The person the relationship is described from."), true),
                ("other_person_id", Schema.String("The relative."), true)),
            GetRelationship),

        Read("get_timeline", "Timeline",
            "A person's life in date order: their own facts with their age, and optionally the births, marriages and deaths of close family and historical events of the time.",
            Schema.Object(
                ("person_id", Schema.String("Whose timeline."), true),
                ("family", Schema.Boolean("Include close family's events (default true)."), false),
                ("historical", Schema.Boolean("Include historical events (default false)."), false)),
            GetTimeline),

        Read("list_places", "List places",
            "Places in the tree with their ids, coordinates and how many facts are at each, optionally filtered by name.",
            Schema.Object(
                ("name", Schema.String("Part of the place name."), false),
                ("offset", Schema.Integer("Skip this many."), false),
                ("limit", Schema.Integer($"Most to return (default {DefaultLimit}, at most {MaxLimit})."), false)),
            ListPlaces),

        Read("get_place", "Read a place",
            "One place: its full name, coordinates, notes, and the facts at it in date order with whose they are.",
            Schema.Object(("place_id", Schema.String("The place's id."), true)), GetPlace),

        Read("list_sources", "List sources",
            "Sources in the tree with their ids, authors and how many citations each has, optionally filtered by title or author.",
            Schema.Object(("text", Schema.String("Part of the title or author."), false)), ListSources),

        Read("get_source", "Read a source",
            "One source: title, author, publication details, call number, repository, comments, and every citation of it with the fact and whose it is.",
            Schema.Object(("source_id", Schema.String("The source's id."), true)), GetSource),

        Read("list_repositories", "List repositories",
            "Repositories (archives, libraries, websites holding sources) with their ids and addresses.",
            Schema.Object(), _ => new JsonArray(Doc.Data.Repositories.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => (JsonNode)Compact(new JsonObject { ["id"] = r.Id, ["name"] = r.Name, ["address"] = r.Address, ["phone"] = r.Phone, ["email"] = r.Email })).ToArray())),

        Read("list_media", "List media",
            "Media items (photos, documents) with their ids, captions, dates, file paths, whether the file is found, and the people in each, optionally filtered by caption.",
            Schema.Object(("text", Schema.String("Part of the caption or description."), false)), ListMedia),

        Read("list_fact_types", "List fact types",
            "Every fact type a person or couple can have, built in and added for this tree, with whether it takes a date and a place.",
            Schema.Object(), _ => new JsonObject
            {
                ["person"] = new JsonArray(FactTypes.PersonTypes.Select(TypeJson).ToArray()),
                ["couple"] = new JsonArray(FactTypes.CoupleTypes.Select(TypeJson).ToArray()),
            }),

        Read("list_historical_events", "List historical events",
            "The historical events this tree shows on timelines, with dates, regions and categories.",
            Schema.Object(), _ => new JsonArray(Doc.Data.HistoricalEvents.OrderBy(h => GenDate.Parse(h.Date).SortKey ?? long.MaxValue)
                .Select(h => (JsonNode)Compact(new JsonObject { ["id"] = h.Id, ["date"] = h.Date, ["end_date"] = h.EndDate, ["title"] = h.Title, ["region"] = h.Region, ["category"] = h.Category, ["description"] = h.Description })).ToArray())),

        Read("check_data_errors", "Check data errors",
            "Runs the program's data checks (parents' ages, dates out of order, unlikely ages, possible duplicate facts, missing details) for one person or the whole tree.",
            Schema.Object(("person_id", Schema.String("One person; leave out for the whole tree."), false)), CheckDataErrors),

        Read("find_duplicates", "Find duplicate people",
            "Pairs of people who may be the same person, closest first, with the reason. merge_people merges a pair.",
            Schema.Object(("limit", Schema.Integer("Most pairs (default 20)."), false)), FindDuplicates),

        Read("tree_statistics", "Tree statistics",
            "Counts of people, living people, families, places, sources and media; the commonest surnames and places; the span of birth years; people with no birth date.",
            Schema.Object(), _ => Statistics()),

        Read("find_text", "Find text",
            "Every field holding some text (names, fact descriptions and notes, notes, place names, source titles, authors, publishers and comments, citation details, media captions): a preview for replace_text.",
            Schema.Object(
                ("text", Schema.String("The text to find."), true),
                ("match_case", Schema.Boolean("Match capitals exactly (default false)."), false),
                ("whole_word", Schema.Boolean("Whole words only (default false)."), false)),
            FindText),

        Read("parse_date", "Read a date",
            "How the program reads a date as typed (\"Abt. 1850\", \"Bet. 1850 and 1855\", \"3/4/1890\", \"1720/21\"): whether it is valid, how it is shown, its year and the weekday of a full date.",
            Schema.Object(("date", Schema.String("The date as text."), true)), ParseDate),

        Read("date_calculator", "Date calculator",
            "The program's date calculator: the age between two dates (age_between), a birth date from an event date and an age (birth_from_age), or an event date from a birth date and an age (event_from_age).",
            Schema.Object(
                ("mode", Schema.String("What to work out.", "age_between", "birth_from_age", "event_from_age"), true),
                ("date", Schema.String("The first date: the earlier date, the event date or the birth date."), true),
                ("other_date", Schema.String("For age_between: the second date."), false),
                ("years", Schema.Integer("For the age modes: years."), false),
                ("months", Schema.Integer("For the age modes: months."), false),
                ("days", Schema.Integer("For the age modes: days."), false)),
            DateCalculator),

        Read("soundex", "Soundex",
            "The Soundex code of a surname and the surnames in the tree that share it (spelling variants in old records).",
            Schema.Object(("surname", Schema.String("The surname."), true)), args =>
            {
                var name = args.Str("surname") ?? throw new ArgumentException("surname is required.");
                var code = Soundex.Code(name);
                var same = Doc.Data.People.Select(p => p.Surname).Where(s => s.Length > 0 && Soundex.Code(s) == code)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase);
                return new JsonObject { ["surname"] = name, ["code"] = code, ["surnames_in_tree"] = new JsonArray(same.Select(s => (JsonNode)s).ToArray()) };
            }),
    ];

    private static JsonNode TypeJson(FactType t) => Compact(new JsonObject
    {
        ["name"] = t.Name,
        ["gedcom_tag"] = t.Tag,
        ["takes_date"] = t.HasDate,
        ["takes_place"] = t.HasPlace,
        ["added_for_this_tree"] = FactTypes.IsCustom(t.Id) ? true : null,
    });

    private static (int Offset, int Limit) Page(JsonObject args, int fallback = DefaultLimit) =>
        (Math.Max(0, args.Int("offset", 0)), Math.Clamp(args.Int("limit", fallback), 1, MaxLimit));

    /// <summary>Born and died, briefly, when the person's details may be shown.</summary>
    private JsonObject Vitals(Person p)
    {
        var json = Ref(p);
        if (Withheld(p) is not null) return json;
        string Brief(Fact? f) => f is null || !FactVisible(f) ? "" : string.Join(", ", new[] { f.ParsedDate.Display, Doc.PlaceName(f.PlaceId) }.Where(s => s.Length > 0));
        json["born"] = Brief(p.Birth);
        json["died"] = Brief(p.Death);
        return Compact(json);
    }

    private JsonNode SearchPeople(JsonObject args)
    {
        var name = args.Str("name");
        var soundex = args.Str("surname_soundex") is { } s ? Soundex.Code(s) : null;
        Sex? sex = args.Str("sex") is null ? null : EnumArg(args, "sex", Sex.Unknown);
        var bornFrom = args.Int("born_from", int.MinValue);
        var bornTo = args.Int("born_to", int.MaxValue);
        var diedFrom = args.Int("died_from", int.MinValue);
        var diedTo = args.Int("died_to", int.MaxValue);
        var place = args.Str("place");
        bool? living = args["living"] is null ? null : args.Bool("living", false);
        bool dated = bornFrom != int.MinValue || bornTo != int.MaxValue || diedFrom != int.MinValue || diedTo != int.MaxValue || place is not null;

        bool Matches(Person p)
        {
            if (name is not null)
            {
                var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!words.All(w => p.DisplayName.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.IndexName.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
                    return false;
            }
            if (soundex is not null && Soundex.Code(p.Surname) != soundex) return false;
            if (sex is not null && p.Sex != sex) return false;
            if (living is not null && Living.IsLiving(Doc, p) != living) return false;
            // Withheld people can't be found by the details withheld.
            if (dated && Withheld(p) is not null) return false;
            var born = p.Birth?.ParsedDate.Year;
            var died = p.Death?.ParsedDate.Year;
            if ((bornFrom != int.MinValue || bornTo != int.MaxValue) && (born is null || born < bornFrom || born > bornTo)) return false;
            if ((diedFrom != int.MinValue || diedTo != int.MaxValue) && (died is null || died < diedFrom || died > diedTo)) return false;
            if (place is not null && !p.Facts.Any(f => FactVisible(f) && Doc.PlaceName(f.PlaceId).Contains(place, StringComparison.CurrentCultureIgnoreCase)))
                return false;
            return true;
        }

        var matches = IndexSort.Sort(Doc, Doc.Data.People.Where(Matches), IndexSortOrder.FamilyName);
        var (offset, limit) = Page(args);
        var page = matches.Skip(offset).Take(limit).ToList();
        var result = new JsonObject
        {
            ["matching"] = matches.Count,
            ["returned"] = page.Count,
            ["people"] = new JsonArray(page.Select(p => (JsonNode)Vitals(p)).ToArray()),
        };
        if (offset + page.Count < matches.Count) result["next_offset"] = offset + page.Count;
        return result;
    }

    private JsonNode GetPerson(JsonObject args)
    {
        var p = PersonArg(args);
        var doc = Doc;
        var why = Withheld(p);
        var json = Ref(p);
        var home = doc.HomePerson;

        // Relationships are given even for withheld people: names and links only.
        var parents = new JsonArray();
        foreach (var f in doc.ParentFamilies(p))
        {
            var link = f.Children.First(c => c.PersonId == p.Id);
            parents.Add(Compact(new JsonObject
            {
                ["family_id"] = f.Id,
                ["father"] = RefOrNull(doc.PartnerBySex(f, Sex.Male, 1)),
                ["mother"] = RefOrNull(doc.PartnerBySex(f, Sex.Female, 2)),
                ["relationship_to_partner1"] = link.Nature1.ToString(),
                ["relationship_to_partner2"] = link.Nature2.ToString(),
            }));
        }
        var families = new JsonArray();
        var spouseFamilies = p.SpouseOrder.Count > 0 ? doc.SpouseFamilies(p) : FactOrder.ByMarriage(doc.SpouseFamilies(p));
        foreach (var f in spouseFamilies)
        {
            var partner = doc.GetPerson(f.OtherPartner(p.Id));
            var fam = Compact(new JsonObject
            {
                ["family_id"] = f.Id,
                ["partner"] = RefOrNull(partner),
                ["couple_type"] = f.Type.ToString(),
                ["status"] = f.Status.ToString(),
            });
            if (why is null && (partner is null || Withheld(partner) is null) && f.Marriage is { } m && FactVisible(m))
                fam["marriage"] = FactJson(m);
            fam["children"] = new JsonArray(doc.Children(f).Select(c => (JsonNode)Ref(c)).ToArray());
            families.Add(fam);
        }
        json["parents"] = parents;
        json["families"] = families;
        if (home is not null && home != p)
            json["relationship_to_home_person"] = $"{(IdOnly(p) ? "This person" : p.DisplayName)} is {(IdOnly(home) ? "the home person" : home.DisplayName)}'s {Kinship.Describe(doc, home, p, withPath: false).Text}";
        else if (home == p)
            json["home_person"] = true;
        if (_state.IsBookmarked(p)) json["bookmarked"] = true;

        if (why is not null)
        {
            json["details_withheld"] = $"This is a {why}: dates, places, facts, notes and media are not given to you (Options › Assistant).  Ask the person for anything you need, and you can still add what they tell you.";
            return json;
        }
        json["given_names"] = p.Given;
        json["surname"] = p.Surname;
        json["suffix"] = p.Suffix;
        json["living"] = Living.IsLiving(doc, p);
        if (p.Private) json["private"] = true;
        json["notes"] = p.Notes;
        json["facts"] = FactsJson(p.Facts, json);
        var media = doc.Data.Media.Where(m => m.PersonIds.Contains(p.Id)).ToList();
        if (media.Count > 0)
            json["media"] = new JsonArray(media.Select(m => (JsonNode)Compact(new JsonObject
            {
                ["id"] = m.Id, ["caption"] = m.Caption, ["portrait"] = p.PhotoMediaId == m.Id ? true : null, ["file_found"] = m.FileExists,
            })).ToArray());
        var warnings = DataChecker.CheckPerson(doc, p).Select(i => i.Message).Distinct().ToList();
        if (warnings.Count > 0) json["data_warnings"] = new JsonArray(warnings.Select(w => (JsonNode)w).ToArray());
        return Compact(json);
    }

    private JsonNode GetFamily(JsonObject args)
    {
        var f = FamilyArg(args);
        var doc = Doc;
        var a = doc.GetPerson(f.Partner1Id);
        var b = doc.GetPerson(f.Partner2Id);
        var json = new JsonObject
        {
            ["id"] = f.Id,
            ["partner1"] = RefOrNull(a),
            ["partner2"] = RefOrNull(b),
            ["couple_type"] = f.Type.ToString(),
            ["status"] = f.Status.ToString(),
        };
        bool withheld = (a is not null && Withheld(a) is not null) || (b is not null && Withheld(b) is not null);
        if (withheld)
            json["details_withheld"] = "A partner's details are withheld, so the couple's facts and notes are too.";
        else
        {
            json["facts"] = FactsJson(f.Facts, json);
            json["notes"] = f.Notes;
        }
        json["children"] = new JsonArray(f.Children.Select(c => doc.GetPerson(c.PersonId) is { } child
            ? (JsonNode)Compact(new JsonObject
            {
                ["person"] = Ref(child),
                ["relationship_to_partner1"] = c.Nature1.ToString(),
                ["relationship_to_partner2"] = c.Nature2.ToString(),
            })
            : null).OfType<JsonNode>().ToArray());
        return Compact(json);
    }

    private JsonNode GetAncestors(JsonObject args)
    {
        var p = PersonArg(args);
        var generations = Math.Clamp(args.Int("generations", 4), 1, 7);
        var list = new JsonArray();
        var level = new List<(long Number, Person Person)> { (1, p) };
        for (int g = 1; g <= generations && level.Count > 0; g++)
        {
            var next = new List<(long, Person)>();
            foreach (var (number, person) in level)
            {
                var entry = Vitals(person);
                entry["number"] = number;
                entry["generation"] = g;
                list.Add(entry);
                if (Doc.Father(person) is { } father) next.Add((number * 2, father));
                if (Doc.Mother(person) is { } mother) next.Add((number * 2 + 1, mother));
            }
            level = next;
        }
        return new JsonObject { ["generations"] = generations, ["ancestors"] = list };
    }

    private JsonNode GetDescendants(JsonObject args)
    {
        var p = PersonArg(args);
        var generations = Math.Clamp(args.Int("generations", 3), 1, 5);
        var list = new JsonArray();
        var seen = new HashSet<string> { p.Id };
        void Walk(Person person, int generation)
        {
            if (generation > generations) return;
            foreach (var f in Doc.SpouseFamilies(person))
                foreach (var child in Doc.Children(f))
                {
                    if (!seen.Add(child.Id)) continue;
                    var entry = Vitals(child);
                    entry["generation"] = generation;
                    entry["family_id"] = f.Id;
                    entry["parents"] = Doc.CoupleName(f);
                    list.Add(entry);
                    Walk(child, generation + 1);
                }
        }
        Walk(p, 1);
        return new JsonObject { ["person"] = Ref(p), ["generations"] = generations, ["descendants"] = list };
    }

    private JsonNode GetRelationship(JsonObject args)
    {
        var a = PersonArg(args);
        var b = PersonArg(args, "other_person_id");
        var result = Kinship.Describe(Doc, a, b);
        string Name(Person p) => IdOnly(p) ? "a living person" : p.DisplayName;
        return new JsonObject
        {
            ["relationship"] = result.Text == "unrelated" ? $"{Name(b)} is not related to {Name(a)} through the tree" : $"{Name(b)} is {Name(a)}'s {result.Text}",
            ["path"] = new JsonArray(result.Path.Select(step =>
            {
                var r = Ref(step.Person);
                r["relation_to_first"] = step.Relation;
                return (JsonNode)r;
            }).ToArray()),
        };
    }

    private JsonNode GetTimeline(JsonObject args)
    {
        var p = PersonArg(args);
        if (Withheld(p) is { } why)
            return new JsonObject { ["person"] = Ref(p), ["details_withheld"] = $"This is a {why}, so their timeline is not given to you." };
        var entries = Timeline.Build(Doc, p, args.Bool("family", true), args.Bool("historical", false));
        return new JsonObject
        {
            ["person"] = Ref(p),
            ["entries"] = new JsonArray(entries
                .Where(e => e.Person is null || e.Person == p || Withheld(e.Person) is null)
                .Select(e => (JsonNode)Compact(new JsonObject
                {
                    ["date"] = e.Date.Display,
                    ["kind"] = e.Kind.ToString(),
                    ["title"] = e.Title,
                    ["detail"] = e.Detail,
                    ["age"] = e.Age,
                    ["person_id"] = e.Person is { } who && who != p ? who.Id : null,
                })).ToArray()),
        };
    }

    /// <summary>Whether a fact's owner (a person, or a couple whose partners) may be shown.</summary>
    private bool OwnerVisible(object owner) => owner switch
    {
        Person p => Withheld(p) is null,
        Family f => new[] { Doc.GetPerson(f.Partner1Id), Doc.GetPerson(f.Partner2Id) }.OfType<Person>().All(x => Withheld(x) is null),
        _ => true,
    };

    private JsonNode ListPlaces(JsonObject args)
    {
        var name = args.Str("name");
        var counts = Doc.AllFacts().Where(x => x.Fact.PlaceId is not null).GroupBy(x => x.Fact.PlaceId!).ToDictionary(g => g.Key, g => g.Count());
        var places = Doc.Data.Places.Where(p => name is null || p.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var (offset, limit) = Page(args);
        var page = places.Skip(offset).Take(limit);
        var result = new JsonObject
        {
            ["matching"] = places.Count,
            ["places"] = new JsonArray(page.Select(p => (JsonNode)Compact(new JsonObject
            {
                ["id"] = p.Id, ["name"] = p.Name, ["latitude"] = p.Latitude, ["longitude"] = p.Longitude,
                ["facts_here"] = counts.TryGetValue(p.Id, out var n) ? n : 0,
            })).ToArray()),
        };
        if (offset + limit < places.Count) result["next_offset"] = offset + limit;
        return result;
    }

    private JsonNode GetPlace(JsonObject args)
    {
        var place = PlaceArg(args);
        var facts = Doc.AllFacts().Where(x => x.Fact.PlaceId == place.Id).ToList();
        var shown = facts.Where(x => OwnerVisible(x.Owner) && FactVisible(x.Fact))
            .OrderBy(x => x.Fact.ParsedDate.SortKey ?? long.MaxValue).ToList();
        var json = Compact(new JsonObject
        {
            ["id"] = place.Id, ["name"] = place.Name, ["latitude"] = place.Latitude, ["longitude"] = place.Longitude, ["notes"] = place.Notes,
        });
        json["facts"] = new JsonArray(shown.Select(x => (JsonNode)Compact(new JsonObject
        {
            ["fact_id"] = x.Fact.Id,
            ["type"] = x.Fact.Type.Name,
            ["date"] = x.Fact.ParsedDate.Display,
            ["whose"] = Doc.OwnerName(x.Owner),
            ["person_id"] = x.Owner is Person p ? p.Id : null,
            ["family_id"] = x.Owner is Family f ? f.Id : null,
        })).ToArray());
        if (shown.Count < facts.Count) json["facts_withheld"] = facts.Count - shown.Count;
        return json;
    }

    private JsonNode ListSources(JsonObject args)
    {
        var text = args.Str("text");
        var counts = Doc.AllFacts().SelectMany(x => x.Fact.Citations).GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.Count());
        return new JsonArray(Doc.Data.Sources
            .Where(s => text is null || s.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase) || s.Author.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => (JsonNode)Compact(new JsonObject
            {
                ["id"] = s.Id, ["title"] = s.ToString(), ["author"] = s.Author, ["citations"] = counts.TryGetValue(s.Id, out var n) ? n : 0,
            })).ToArray());
    }

    private JsonNode GetSource(JsonObject args)
    {
        var s = SourceArg(args);
        var repo = Doc.GetRepository(s.RepositoryId);
        var json = Compact(new JsonObject
        {
            ["id"] = s.Id, ["title"] = s.Title, ["author"] = s.Author, ["publisher"] = s.Publisher, ["publisher_location"] = s.PublisherLocation,
            ["publish_date"] = s.PublishDate, ["call_number"] = s.CallNumber, ["comments"] = s.Comments, ["url"] = s.Url,
            ["repository"] = repo is null ? null : Compact(new JsonObject { ["id"] = repo.Id, ["name"] = repo.Name }),
        });
        var cited = Doc.AllFacts().SelectMany(x => x.Fact.Citations.Where(c => c.SourceId == s.Id).Select(c => (x.Owner, x.Fact, Citation: c))).ToList();
        var shown = cited.Where(x => OwnerVisible(x.Owner) && FactVisible(x.Fact)).ToList();
        json["citations"] = new JsonArray(shown.Select(x => (JsonNode)Compact(new JsonObject
        {
            ["fact_id"] = x.Fact.Id,
            ["fact"] = x.Fact.Type.Name,
            ["whose"] = Doc.OwnerName(x.Owner),
            ["person_id"] = x.Owner is Person p ? p.Id : null,
            ["detail"] = x.Citation.Detail,
            ["url"] = x.Citation.Url,
        })).ToArray());
        if (shown.Count < cited.Count) json["citations_withheld"] = cited.Count - shown.Count;
        return json;
    }

    private JsonNode ListMedia(JsonObject args)
    {
        var text = args.Str("text");
        return new JsonArray(Doc.Data.Media
            .Where(m => text is null || m.Caption.Contains(text, StringComparison.CurrentCultureIgnoreCase) || m.Description.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .Select(m => (JsonNode)Compact(new JsonObject
            {
                ["id"] = m.Id, ["caption"] = m.Caption, ["date"] = m.Date, ["category"] = m.Category, ["description"] = m.Description,
                ["file"] = m.FilePath, ["file_found"] = m.FileExists,
                ["people"] = new JsonArray(m.PersonIds.Select(Doc.GetPerson).OfType<Person>().Select(p => (JsonNode)Ref(p)).ToArray()),
            })).ToArray());
    }

    private JsonNode CheckDataErrors(JsonObject args)
    {
        var people = args.Str("person_id") is not null ? [PersonArg(args)] : Doc.Data.People.OrderBy(p => p.IndexName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var list = new JsonArray();
        int withheld = 0, total = 0;
        foreach (var p in people)
        {
            var issues = DataChecker.CheckPerson(Doc, p).DistinctBy(i => i.Message).ToList();
            if (issues.Count == 0) continue;
            total += issues.Count;
            if (Withheld(p) is not null) { withheld += issues.Count; continue; }
            if (list.Count >= 300) continue;
            var entry = Ref(p);
            entry["issues"] = new JsonArray(issues.Select(i => (JsonNode)new JsonObject { ["category"] = i.Category, ["message"] = i.Message }).ToArray());
            list.Add(entry);
        }
        var result = new JsonObject { ["issues_found"] = total, ["people"] = list };
        if (withheld > 0) result["issues_withheld"] = $"{withheld} issues concern people whose details are withheld; Tools > Find data errors shows them to the person.";
        return result;
    }

    private JsonNode FindDuplicates(JsonObject args)
    {
        var limit = Math.Clamp(args.Int("limit", 20), 1, MaxLimit);
        var pairs = PersonMerge.FindDuplicates(Doc);
        return new JsonObject
        {
            ["pairs_found"] = pairs.Count,
            ["pairs"] = new JsonArray(pairs.Take(limit).Select(c => (JsonNode)new JsonObject
            {
                ["first"] = Vitals(c.A),
                ["second"] = Vitals(c.B),
                ["reason"] = Withheld(c.A) is null && Withheld(c.B) is null ? c.Reason : "(details withheld)",
                ["score"] = c.Score,
            }).ToArray()),
        };
    }

    private JsonNode Statistics()
    {
        var doc = Doc;
        var c = doc.Counts();
        var people = doc.Data.People;
        var visible = people.Where(p => Withheld(p) is null).ToList();
        var years = visible.Select(p => p.Birth?.ParsedDate.Year).OfType<int>().ToList();
        var surnames = people.Where(p => p.Surname.Length > 0).GroupBy(p => p.Surname, StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(g => g.Count()).Take(15).Select(g => (JsonNode)new JsonObject { ["surname"] = g.Key, ["people"] = g.Count() });
        var places = doc.AllFacts().Where(x => x.Fact.PlaceId is not null && OwnerVisible(x.Owner)).GroupBy(x => x.Fact.PlaceId!)
            .OrderByDescending(g => g.Count()).Take(10).Select(g => (JsonNode)new JsonObject { ["place"] = doc.PlaceName(g.Key), ["facts"] = g.Count() });
        return Compact(new JsonObject
        {
            ["people"] = c.People,
            ["living"] = people.Count(p => Living.IsLiving(doc, p)),
            ["families"] = c.Families,
            ["places"] = c.Places,
            ["sources"] = c.Sources,
            ["repositories"] = doc.Data.Repositories.Count,
            ["media"] = c.Media,
            ["birth_years"] = years.Count > 0 ? $"{years.Min()}-{years.Max()}" : null,
            ["people_without_birth_date"] = visible.Count(p => p.Birth?.ParsedDate.IsValid != true),
            ["commonest_surnames"] = new JsonArray(surnames.ToArray()),
            ["commonest_places"] = new JsonArray(places.ToArray()),
        });
    }

    private JsonNode FindText(JsonObject args)
    {
        var text = args.Str("text") ?? throw new ArgumentException("text is required.");
        var hits = TreeTools.Find(Doc, text, args.Bool("match_case", false), args.Bool("whole_word", false));
        return new JsonObject
        {
            ["found"] = hits.Count,
            ["fields"] = new JsonArray(hits.Take(MaxLimit).Select(h => (JsonNode)Compact(new JsonObject
            {
                ["owner"] = h.Person is { } p && IdOnly(p) ? "Living person" : h.Owner,
                ["person_id"] = h.Person?.Id,
                ["field"] = h.Field,
                ["text"] = h.Person is { } q && Withheld(q) is not null ? "(withheld)" : h.Text,
            })).ToArray()),
        };
    }

    private static JsonNode ParseDate(JsonObject args)
    {
        var text = args.Str("date") ?? throw new ArgumentException("date is required.");
        var d = GenDate.Parse(text);
        var json = new JsonObject
        {
            ["typed"] = text,
            ["valid"] = d.IsValid,
            ["shown_as"] = d.Display,
        };
        if (d.IsValid)
        {
            json["modifier"] = d.Modifier.ToString();
            json["range"] = d.IsRange;
            json["year"] = d.Year;
            if (d.Start is { Year: int y, Month: int m, Day: int day } && !d.IsRange)
                json["weekday"] = GenDate.WeekdayName(GenDate.ToDayNumber(y, m, day));
        }
        else
        {
            json["hint"] = "Forms the program reads: 12 Mar 1890, Mar 1890, 1890, Abt. 1850, Bef. 1850, Aft. 1850, Cal. 1850, Bet. 1850 and 1855, From 1850 to 1855, 1720/21, 3/4/1890 (day first by default), 44 BC.";
        }
        return json;
    }

    private static JsonNode DateCalculator(JsonObject args)
    {
        var mode = args.Str("mode") ?? throw new ArgumentException("mode is required.");
        (int Y, int M, int D) Full(string name)
        {
            var d = GenDate.Parse(args.Str(name) ?? throw new ArgumentException($"{name} is required."));
            if (!d.IsValid || d.IsRange || d.Start is not { Year: int y })
                throw new ArgumentException($"{name} must be a single date the program can read (use parse_date to check it).");
            return (y, d.Start.Month ?? 1, d.Start.Day ?? 1);
        }
        // Written in the program's own date form, with English month names whatever the computer's language.
        static string Typed(int y, int m, int d) => $"{d} {System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m)} {Math.Abs(y)}{(y < 0 ? " BC" : "")}";
        static string Show(int y, int m, int d) => $"{GenDate.Parse(Typed(y, m, d)).Display} ({GenDate.WeekdayName(GenDate.ToDayNumber(y, m, d))})";
        int years = args.Int("years", 0), months = args.Int("months", 0), days = args.Int("days", 0);
        switch (mode)
        {
            case "age_between":
            {
                var a = Full("date");
                var b = Full("other_date");
                return new JsonObject { ["age"] = Age.Between(a.Y, a.M, a.D, b.Y, b.M, b.D).ToString() };
            }
            case "birth_from_age":
            {
                var e = Full("date");
                var (y, m, d) = Age.Subtract(e.Y, e.M, e.D, years, months, days);
                return new JsonObject { ["birth"] = Show(y, m, d), ["record_as"] = $"Cal. {Typed(y, m, d)}" };
            }
            case "event_from_age":
            {
                var b = Full("date");
                var (y, m, d) = Age.Add(b.Y, b.M, b.D, years, months, days);
                return new JsonObject { ["event"] = Show(y, m, d) };
            }
            default:
                throw new ArgumentException("mode must be age_between, birth_from_age or event_from_age.");
        }
    }
}
