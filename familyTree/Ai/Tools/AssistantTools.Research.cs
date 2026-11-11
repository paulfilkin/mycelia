using System.Net.Http;
using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;
using Mycelia.Ai.Mcp;
using Mycelia.Ai.Research;

namespace Mycelia.Ai.Tools;

/// <summary>
/// The research tools (TDD 25.3 and 25.4): where to look (suggest_resources, from the built-in resources list), how
/// the open archive APIs are called (api_notes), and fetch_url, which reads them without asking and asks for any other
/// address.
/// </summary>
public sealed partial class AssistantTools
{
    /// <summary>The resources list's rows that are one of fetch_url's listed APIs, by the start of their name.</summary>
    private static readonly (string Name, string Api)[] ListedResources =
    [
        ("WikiTree", "wikitree"), ("Wikidata", "wikidata"), ("Open Archives", "openarchives"),
        ("The National Archives - Discovery", "tna"), ("Trove", "trove"), ("Papers Past", "digitalnz"),
        ("Europeana", "europeana"), ("GeoNames", "geonames"), ("Internet Archive", "internetarchive"),
    ];

    private const int MaxResources = 60;
    private const int DefaultFetchChars = 20000;
    private const int MaxFetchChars = 38000;

    private AssistantTool? _fetchTool;

    private IEnumerable<AssistantTool> ResearchTools()
    {
        yield return Read("suggest_resources", "Suggest where to look",
            "Suggests genealogy websites and archives to search, from Mycelia's built-in list (compiled " + ResourceList.Compiled + "): for a country or region and a kind of record, with each site's cost, how it is used, whether it has an API or an MCP server, and notes. Sites marked with an API id can be read with fetch_url (see api_notes). Use it before searching, and tell the person where else to look by hand.",
            Schema.Object(
                ("place", Schema.String("A country or region, such as Ireland, Germany, the Netherlands, Australia."), false),
                ("record_type", Schema.String("A kind of record or topic, such as parish registers, census, wills, military, newspapers, DNA, emigration."), false),
                ("free_only", Schema.Boolean("Only free sites (default false)."), false),
                ("with_api", Schema.Boolean("Only sites with an open API (default false)."), false),
                ("with_mcp", Schema.Boolean("Only sites with an MCP server (default false)."), false),
                ("include_worldwide", Schema.Boolean("Keep worldwide sites when a place is given (default true)."), false)),
            args =>
            {
                var found = ResourceList.Find(args.Str("place"), args.Str("record_type"), args.Bool("free_only", false),
                    args.Bool("with_api", false), args.Bool("with_mcp", false), args.Bool("include_worldwide", true));
                var list = new JsonArray();
                foreach (var r in found.Take(MaxResources))
                {
                    var row = new JsonObject
                    {
                        ["name"] = r.Name, ["website"] = r.Website, ["section"] = r.Section, ["use"] = r.Use, ["focus"] = r.Focus,
                        ["cost"] = r.Cost, ["api"] = r.Api, ["mcp"] = r.Mcp,
                    };
                    if (r.Notes.Length > 0) row["notes"] = r.Notes;
                    if (ListedResources.FirstOrDefault(l => r.Name.StartsWith(l.Name, StringComparison.OrdinalIgnoreCase)) is { Api: { } api })
                        row["fetch_url_api"] = api;
                    list.Add(row);
                }
                return new JsonObject
                {
                    ["compiled"] = ResourceList.Compiled,
                    ["how_to_read"] = new JsonArray(ResourceList.Legend.Select(l => (JsonNode)l).ToArray()),
                    ["practical_notes"] = new JsonArray(ResourceList.Practical.Select(l => (JsonNode)l).ToArray()),
                    ["found"] = found.Count,
                    ["resources"] = list,
                    ["more"] = found.Count > MaxResources ? $"Only the first {MaxResources} are listed: narrow by place or record_type." : null,
                    ["community_servers"] = "An MCP server marked Community is run by someone other than the site: searches pass through it, and it may stop working. Say so before suggesting one.",
                };
            }, "Listed");

        yield return Read("api_notes", "How to call an archive API",
            "How to call the open archive APIs fetch_url can read without asking: their addresses, parameters, limits and how to cite what they give. Without an api, lists them with whether each needs a key and has one.",
            Schema.Object(("api", Schema.String("The API's id; all of them listed briefly if left out.", ResearchApis.All.Select(a => a.Id).ToArray()), false)),
            args =>
            {
                if (args.Str("api") is { } id)
                {
                    var api = ResearchApis.Get(id) ?? throw new ArgumentException($"There is no API {id}: api_notes without an api lists them.");
                    return new JsonObject
                    {
                        ["id"] = api.Id, ["name"] = api.Name, ["hosts"] = new JsonArray(api.Hosts.Select(h => (JsonNode)h).ToArray()),
                        ["key"] = KeyStatus(api), ["notes"] = api.Notes,
                    };
                }
                return new JsonObject
                {
                    ["apis"] = new JsonArray(ResearchApis.All.Select(a => (JsonNode)new JsonObject
                    {
                        ["id"] = a.Id, ["name"] = a.Name, ["hosts"] = new JsonArray(a.Hosts.Select(h => (JsonNode)h).ToArray()), ["key"] = KeyStatus(a),
                    }).ToArray()),
                    ["not_listed"] = new JsonArray(ResearchApis.LeftOut.Select(l => (JsonNode)$"{l.Name}: {l.Reason}.").ToArray()),
                    ["other_addresses"] = "fetch_url asks the person before fetching any other address.",
                };
            }, "Notes given");

        _fetchTool = new AssistantTool
        {
            Name = "fetch_url",
            Title = "Read a web address",
            Description = "Reads a web address (GET only) and gives back its text: JSON, XML or plain text as it is, a web page as plain text, in slices of up to " + MaxFetchChars + " characters. The listed archive APIs (api_notes) and the sites the person always allows run without asking, with any key added by Mycelia; any other address asks the person first. Never put living people's details in an address. Images and PDFs: download_file.",
            Schema = Schema.Object(
                ("url", Schema.String("The full web address, with its parameters URL-encoded."), true),
                ("start", Schema.Integer("Where to start in the text, for reading on (the previous answer gives next_start)."), false),
                ("max_chars", Schema.Integer($"How much text to give back (default {DefaultFetchChars}, at most {MaxFetchChars})."), false)),
            DefaultPolicy = ToolPolicy.Ask,
            ReachesWeb = true,
            AllowWithoutAsking = args => WebFetch.IsListed(args.Str("url")) || SiteAllowed(args.Str("url")),
            // "Always allow" remembers this site, not the whole tool: other sites still ask (TDD 25.4).
            AllowAlwaysCall = args =>
            {
                if (!Uri.TryCreate(args.Str("url"), UriKind.Absolute, out var site) || site.UserInfo.Length > 0
                    || (site.Scheme != Uri.UriSchemeHttps && site.Scheme != Uri.UriSchemeHttp))
                    return false;
                if (!Settings.SiteAllowed(site))
                    Settings.AllowedSites.Add(AssistantSettings.SiteOf(site));
                return true;
            },
            AlwaysLabel = "Always allow this site",
            AlwaysOutcome = "Allowed (always for this site)",
            Run = FetchUrlAsync,
            Describe = args => Uri.TryCreate(args.Str("url"), UriKind.Absolute, out var u)
                ? $"Read {u}.  It isn't one of the listed archive APIs, so this address, and anything in it, goes to {u.Host}."
                : $"Read {args.Str("url")}.",
        };
        yield return _fetchTool;
    }

    /// <summary>The address is on a site the person always allows (Options › Research).</summary>
    private bool SiteAllowed(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && Settings.SiteAllowed(uri);

    private string KeyStatus(ResearchApi api) =>
        !api.NeedsKey ? "none needed"
        : Settings.GetResearchKey(api.Id).Length > 0 ? "set (Mycelia adds it)"
        : $"not set: the person can add a free {(api.KeyLabel.StartsWith("API") ? "key" : api.KeyLabel)} in Options › Research ({api.SignUp})";

    private async Task<ToolOutcome> FetchUrlAsync(JsonObject args, CancellationToken cancel)
    {
        var uri = WebFetch.Parse(args.Str("url"));
        var api = ResearchApis.For(uri);
        if (api is { NeedsKey: true } && uri.Scheme == Uri.UriSchemeHttps && Settings.GetResearchKey(api.Id).Length == 0)
            return new ToolOutcome($"{api.Name} needs {(api.KeyLabel.StartsWith("API") ? "an" : "a")} {api.KeyLabel}, and none is set.  The person can add a free one in Options › Research ({api.SignUp}).", true, "No key set");
        // A call that ran without asking, because its address is listed or its site always allowed, follows redirects
        // only to listed addresses and allowed sites.
        bool unasked = _fetchTool?.Policy == ToolPolicy.Ask && (WebFetch.IsListed(uri.ToString()) || SiteAllowed(uri.ToString()));
        Func<Uri, bool>? stayWithin = unasked ? u => WebFetch.Listed(u) || (u.UserInfo.Length == 0 && Settings.SiteAllowed(u)) : null;
        FetchResult result;
        try
        {
            result = await WebFetch.GetAsync(WebFetch.Http, uri, Settings.GetResearchKey, stayWithin, cancel);
        }
        catch (WebFetch.RedirectException ex)
        {
            return new ToolOutcome($"{uri.Host} redirected to {ex.Target}, which isn't one of the listed archive APIs or the sites the person always allows.  Call fetch_url with that address to read it; the person will be asked.", true, "Redirected elsewhere");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new ToolOutcome($"{uri.Host} didn't answer in time.", true, "No answer");
        }
        catch (HttpRequestException ex)
        {
            return new ToolOutcome($"{uri.Host} couldn't be reached: {ex.Message}", true, "Not reached");
        }

        int start = Math.Max(0, args.Int("start", 0));
        int max = Math.Clamp(args.Int("max_chars", DefaultFetchChars), 500, MaxFetchChars);
        bool failed = result.Status >= 400;
        if (failed) max = Math.Min(max, 2000);
        var text = start >= result.Text.Length ? "" : result.Text.Substring(start, Math.Min(max, result.Text.Length - start));
        int end = start + text.Length;
        var head = $"{result.Status} {result.Reason}, {(result.ContentType.Length > 0 ? result.ContentType : "no type given")}{(result.IsHtml ? " (a web page, given as plain text)" : "")}, from {result.FinalUrl}";
        head += result.Text.Length == 0 ? ".  No text."
            : $".  Characters {start} to {end} of {result.Text.Length}{(result.Cut ? " (the answer was cut at 2 MB)" : "")}" + (end < result.Text.Length && !failed ? $"; next_start {end}." : ".");
        if (failed && result.Status is 401 or 403 && api is { NeedsKey: true })
            head += $"  The key in Options › Research may be wrong.";
        else if (failed && result.Status == 429)
            head += "  Too many requests: wait before asking again.";
        var summary = failed ? $"{result.Status} {result.Reason}" : $"{result.Text.Length:N0} characters from {new Uri(result.FinalUrl).Host}";
        return new ToolOutcome(head + "\n\n" + text, failed, summary);
    }
}
