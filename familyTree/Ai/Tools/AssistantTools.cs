using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;
using Mycelia.Model;
using Mycelia.Ui;
using Mycelia.Ui.Charts;

namespace Mycelia.Ai.Tools;

/// <summary>What the tools need from the main window besides the tree: moving around, saving, the settings.</summary>
public interface IToolHost
{
    AssistantSettings Settings { get; }

    /// <summary>The workspace on show ("People", "Places"...).</summary>
    string Workspace { get; }

    /// <summary>Shows a workspace; "Person" is the People workspace's Person view.</summary>
    void Navigate(string workspace);

    /// <summary>Opens the Charts workspace on a kind of chart for the selected person.</summary>
    void ShowChart(ChartKind kind, int? generations);

    /// <summary>Saves the tree to its own file, or to <paramref name="path"/>; null when saved, else why not.</summary>
    string? SaveTree(string? path);
}

/// <summary>
/// Mycelia's own tools for the Assistant (TDD 22.3): reading the tree, moving around, changing it and its files.
/// Every tool runs on the UI thread and finds people, families and facts again by id on each call, since undo replaces
/// every object.  Every change is one undo step (<see cref="Change"/>), and the changes of one request can be reverted
/// together (<see cref="BeginRequest"/>).  Living people, and people and facts marked Private, are given to the model
/// as the settings say (<see cref="Withheld"/>, TDD 22.4).  The same list is meant to be served to other programs over
/// MCP later (TDD 22.8).
/// </summary>
public sealed partial class AssistantTools
{
    private readonly AppState _state;
    private readonly IToolHost _host;

    public AssistantTools(AppState state, IToolHost host)
    {
        _state = state;
        _host = host;
        All = [.. ReadTools(), .. MoveTools(), .. ChangeTools(), .. FileTools(), .. ResearchTools()];
    }

    public IReadOnlyList<AssistantTool> All { get; }

    private TreeDocument Doc => _state.Doc;
    private AssistantSettings Settings => _host.Settings;

    // ---- Context ----------------------------------------------------------------------------------

    /// <summary>Where the person is: the tree, the workspace and the selected person.</summary>
    public JsonObject Context()
    {
        var doc = Doc;
        var c = doc.Counts();
        var context = new JsonObject
        {
            ["tree"] = new JsonObject
            {
                ["name"] = doc.Data.Name,
                ["file"] = doc.FilePath,
                ["unsaved_changes"] = doc.IsDirty,
                ["counts"] = new JsonObject
                {
                    ["people"] = c.People,
                    ["families"] = c.Families,
                    ["places"] = c.Places,
                    ["sources"] = c.Sources,
                    ["media"] = c.Media,
                    ["surnames"] = c.Surnames,
                },
            },
            ["workspace"] = _host.Workspace,
            ["date_display"] = GenDate.Parse("5 Mar 1890").Format(GenDate.Style),
            ["living_people"] = Settings.LivingPeople switch
            {
                LivingPeopleDetail.Full => "given in full",
                LivingPeopleDetail.None => "withheld entirely (id only)",
                _ => "names and relationships only; their dates, places, facts and notes are withheld",
            },
        };
        if (_state.Selected is { } selected)
            context["selected_person"] = Ref(selected);
        if (doc.HomePerson is { } home)
            context["home_person"] = Ref(home);
        return context;
    }

    public string ContextLine()
    {
        var selected = _state.Selected?.DisplayName;
        return selected is null ? _host.Workspace : $"{_host.Workspace} · {selected}";
    }

    // ---- What the model may see (TDD 22.4) ------------------------------------------------------------

    private readonly Dictionary<string, bool> _livingCache = [];
    private TreeData? _livingCacheFor;

    /// <summary>
    /// How much of a person the model may see: null for everything, else why their details are withheld.  Living
    /// people (Living.IsLiving) follow Options › Assistant; people marked Private do too unless
    /// "Include people and facts marked Private" is ticked.
    /// </summary>
    private string? Withheld(Person p)
    {
        if (!ReferenceEquals(_livingCacheFor, Doc.Data))
        {
            _livingCache.Clear();
            _livingCacheFor = Doc.Data;
        }
        if (p.Private && !Settings.IncludePrivate)
            return "marked Private";
        if (Settings.LivingPeople == LivingPeopleDetail.Full)
            return null;
        if (!_livingCache.TryGetValue(p.Id, out var living))
            _livingCache[p.Id] = living = Living.IsLiving(Doc, p);
        return living ? "living person" : null;
    }

    /// <summary>The changes of a tool call can make someone living or not; forget what was worked out.</summary>
    private void ForgetLiving() => _livingCache.Clear();

    /// <summary>Whether the model sees only an id for this person (living people with "Not at all", or Private with it).</summary>
    private bool IdOnly(Person p) => Withheld(p) is not null && Settings.LivingPeople == LivingPeopleDetail.None;

    /// <summary>A short reference to a person: id, name, sex and life span, or less when withheld.</summary>
    private JsonObject Ref(Person p)
    {
        var why = Withheld(p);
        if (why is null)
            return Compact(new JsonObject { ["id"] = p.Id, ["name"] = p.DisplayName, ["sex"] = p.Sex.ToString(), ["life_span"] = p.LifeSpan });
        if (IdOnly(p))
            return new JsonObject { ["id"] = p.Id, ["name"] = "Living person", ["withheld"] = why };
        return new JsonObject { ["id"] = p.Id, ["name"] = p.DisplayName, ["sex"] = p.Sex.ToString(), ["withheld"] = why };
    }

    private JsonObject? RefOrNull(Person? p) => p is null ? null : Ref(p);

    /// <summary>Whether a fact may be shown: not when it is Private (unless included) or its owner's details are withheld.</summary>
    private bool FactVisible(Fact f) => !f.Private || Settings.IncludePrivate;

    /// <summary>A fact as the model sees it: the date as typed and as read, the place, and the sources.</summary>
    private JsonObject FactJson(Fact f)
    {
        var date = f.ParsedDate;
        var json = new JsonObject
        {
            ["id"] = f.Id,
            ["type"] = f.Type.Name,
            ["date"] = f.Date,
            ["date_read_as"] = date.IsEmpty ? null : date.IsValid ? date.Display : "(not a date the program can read)",
            ["place"] = Doc.PlaceName(f.PlaceId),
            ["place_id"] = f.PlaceId,
            ["description"] = f.Description,
            ["preferred"] = f.Preferred ? null : false,
            ["private"] = f.Private ? true : null,
            ["note"] = f.Note,
        };
        if (f.Citations.Count > 0)
            json["citations"] = new JsonArray(f.Citations.Select(c => (JsonNode)Compact(new JsonObject
            {
                ["source_id"] = c.SourceId,
                ["source"] = Doc.GetSource(c.SourceId)?.ToString() ?? "(missing source)",
                ["detail"] = c.Detail,
                ["url"] = c.Url,
                ["accessed"] = c.Accessed,
                ["text"] = c.Text,
            })).ToArray());
        return Compact(json);
    }

    /// <summary>The facts the model may see, in the order the person editor lists them, and a count of those left out.</summary>
    private JsonArray FactsJson(IEnumerable<Fact> facts, JsonObject into)
    {
        var all = FactOrder.Sorted(facts).ToList();
        var shown = all.Where(FactVisible).ToList();
        if (shown.Count < all.Count)
            into["private_facts_withheld"] = all.Count - shown.Count;
        return new JsonArray(shown.Select(f => (JsonNode)FactJson(f)).ToArray());
    }

    /// <summary>Drops empty strings and nulls so results stay short.</summary>
    private static JsonObject Compact(JsonObject json)
    {
        foreach (var key in json.Where(p => p.Value is null || (p.Value is JsonValue v && v.TryGetValue(out string? s) && s.Length == 0)).Select(p => p.Key).ToList())
            json.Remove(key);
        return json;
    }

    // ---- Finding things by id or name ---------------------------------------------------------------

    private Person PersonArg(JsonObject args, string name = "person_id")
    {
        var id = args.Str(name) ?? throw new ArgumentException($"{name} is required.");
        return Doc.GetPerson(id) ?? throw new ArgumentException($"There is no person with id {id}.  Use search_people to find ids.");
    }

    private Family FamilyArg(JsonObject args, string name = "family_id")
    {
        var id = args.Str(name) ?? throw new ArgumentException($"{name} is required.");
        return Doc.GetFamily(id) ?? throw new ArgumentException($"There is no family with id {id}.  get_person lists a person's families.");
    }

    private Place PlaceArg(JsonObject args, string name = "place_id")
    {
        var id = args.Str(name) ?? throw new ArgumentException($"{name} is required.");
        return Doc.GetPlace(id) ?? throw new ArgumentException($"There is no place with id {id}.  Use list_places.");
    }

    private Source SourceArg(JsonObject args, string name = "source_id")
    {
        var id = args.Str(name) ?? throw new ArgumentException($"{name} is required.");
        return Doc.GetSource(id) ?? throw new ArgumentException($"There is no source with id {id}.  Use list_sources.");
    }

    /// <summary>A fact by id, with the list it is in and whose it is.</summary>
    private (Fact Fact, List<Fact> List, object Owner) FactArg(JsonObject args, string name = "fact_id")
    {
        var id = args.Str(name) ?? throw new ArgumentException($"{name} is required.");
        foreach (var p in Doc.Data.People)
            if (p.Facts.FirstOrDefault(f => f.Id == id) is { } f) return (f, p.Facts, p);
        foreach (var fam in Doc.Data.Families)
            if (fam.Facts.FirstOrDefault(f => f.Id == id) is { } f) return (f, fam.Facts, fam);
        throw new ArgumentException($"There is no fact with id {id}.  get_person and get_family list facts with their ids.");
    }

    /// <summary>A fact type by its name ("Birth", "Occupation"), its GEDCOM tag ("BIRT") or its id, for a person or a couple.</summary>
    private static FactType FactTypeArg(string? text, FactScope scope)
    {
        var name = (text ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("type is required (a fact type's name, such as Birth or Occupation).");
        var types = scope == FactScope.Person ? FactTypes.PersonTypes : FactTypes.CoupleTypes;
        var found = types.FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    ?? types.FirstOrDefault(t => t.Tag.Equals(name, StringComparison.OrdinalIgnoreCase) && !FactTypes.IsCustom(t.Id))
                    ?? (int.TryParse(name, out var id) ? types.FirstOrDefault(t => t.Id == id) : null);
        if (found is not null) return found;
        // Close names: one holds the other, or a slip of a couple of letters ("Baptsm", "Ocupation").
        var close = types.Where(t => t.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase) || name.Contains(t.Name, StringComparison.CurrentCultureIgnoreCase)
                                     || Distance(t.Name.ToLowerInvariant(), name.ToLowerInvariant()) <= 2)
            .Select(t => t.Name).Take(6).ToList();
        throw new ArgumentException($"There is no {(scope == FactScope.Person ? "person" : "couple")} fact type called \"{name}\"."
            + (close.Count > 0 ? $"  Did you mean: {string.Join(", ", close)}?" : "  list_fact_types lists them; add_fact_type adds one."));
    }

    /// <summary>Edit distance (insertions, deletions and substitutions) between two short words.</summary>
    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int previous = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int current = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = current;
            }
        }
        return row[b.Length];
    }

    private static T EnumArg<T>(JsonObject args, string name, T fallback) where T : struct, Enum
    {
        var text = args.Str(name);
        if (text is null) return fallback;
        if (Enum.TryParse<T>(text.Replace(" ", ""), ignoreCase: true, out var value)) return value;
        throw new ArgumentException($"{name} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static string[] EnumNames<T>() where T : struct, Enum => Enum.GetNames<T>();

    // ---- Changes: one undo step each, and revert by request ----------------------------------------

    private string? _requestCopy;
    private int _requestChanges;

    /// <summary>A request is starting: the tree is copied at its first change, so all of its changes can be reverted.</summary>
    public void BeginRequest()
    {
        _requestCopy = null;
        _requestChanges = 0;
    }

    /// <summary>
    /// A request has ended: how many changes it made and, when it made some, a way to put the tree back as it was
    /// before them.  Reverting is refused once the tree has changed since (Undo still works then).
    /// </summary>
    public (int Changes, Func<string>? Revert) EndRequest()
    {
        if (_requestChanges == 0 || _requestCopy is null) return (0, null);
        var before = _requestCopy;
        var after = Doc.Serialize();
        var doc = Doc;
        _requestCopy = null;
        return (_requestChanges, () =>
        {
            if (!ReferenceEquals(doc, Doc)) return "Another tree is open now, so there is nothing to revert here.";
            if (Doc.Serialize() != after) return "The tree has changed since, so it wasn't reverted.  Undo takes changes back one at a time.";
            Doc.RestoreCopy(before);
            ForgetLiving();
            return "Reverted: the tree is as it was before this request.  Undo brings the changes back.";
        });
    }

    /// <summary>Runs one change as one undo step and counts it for the request's Revert.</summary>
    private string Change(Func<string> change)
    {
        _requestCopy ??= Doc.Serialize();
        string result = "";
        Doc.Change(() => result = change());
        _requestChanges++;
        ForgetLiving();
        return result;
    }

    // ---- Building tools ----------------------------------------------------------------------------

    /// <summary>A tool that reads or moves around (Allow by default).</summary>
    private static AssistantTool Read(string name, string title, string description, JsonObject schema, Func<JsonObject, JsonNode> run, string? summary = null) => new()
    {
        Name = name,
        Title = title,
        Description = description,
        Schema = schema,
        Run = (args, _) => Task.FromResult(new ToolOutcome(run(args).ToJsonString(), false, summary)),
    };

    /// <summary>A tool that changes the tree or its files (Ask by default), with the sentence its approval card shows.</summary>
    private static AssistantTool Write(string name, string title, string description, JsonObject schema, Func<JsonObject, string> run, Func<JsonObject, string> describe) => new()
    {
        Name = name,
        Title = title,
        Description = description,
        Schema = schema,
        Changes = true,
        Run = (args, _) =>
        {
            var message = run(args);
            return Task.FromResult(new ToolOutcome(message, false, message.Length > 120 ? message[..117] + "..." : message));
        },
        Describe = describe,
    };

    /// <summary>A person's name for an approval card, by id (the card is shown before anything is looked up).</summary>
    private string NameOf(JsonObject args, string name = "person_id") =>
        Doc.GetPerson(args.Str(name))?.DisplayName ?? args.Str(name) ?? "someone";

    private string CoupleOf(JsonObject args, string name = "family_id") =>
        Doc.GetFamily(args.Str(name)) is { } f ? Doc.CoupleName(f) : args.Str(name) ?? "a family";

    private static string Quoted(string? text) => string.IsNullOrWhiteSpace(text) ? "" : $"\"{text.Trim()}\"";
}
