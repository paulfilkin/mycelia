namespace Mycelia.Ai.Research;

/// <summary>Where an API takes its key: a request header, or a parameter in the address.</summary>
public enum KeyPlace { None, Header, Parameter }

/// <summary>
/// An open archive API that fetch_url may call without asking (TDD 25.4): its hosts, the key Mycelia adds itself
/// (from Options › Research, so the model never sees it), fixed parameters and headers, and the notes api_notes gives
/// the model.  Each note was checked against the live service on 11 November 2026.
/// </summary>
public sealed record ResearchApi(
    string Id,
    string Name,
    string[] Hosts,
    string Notes,
    KeyPlace KeyPlace = KeyPlace.None,
    string KeyField = "",
    string KeyLabel = "",
    string SignUp = "",
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public bool NeedsKey => KeyPlace != KeyPlace.None;

    /// <summary>The address is on one of the hosts; a host starting with a dot stands for its subdomains.</summary>
    public bool Serves(Uri uri) => Hosts.Any(h => h.StartsWith('.')
        ? uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)
        : string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase));
}

public static class ResearchApis
{
    public static readonly IReadOnlyList<ResearchApi> All =
    [
        new("wikitree", "WikiTree", ["api.wikitree.com"],
            """
            A shared world tree of public profiles. GET https://api.wikitree.com/api.php?action=...  Mycelia adds appId itself.
            - searchPerson: FirstName, LastName, BirthDate and DeathDate (a year or YYYY-MM-DD), limit, fields.  Returns matches with each profile's Name (its WikiTree ID, such as Churchill-4).
            - getPeople: keys (WikiTree IDs, comma separated), ancestors=n or descendants=n for generations, fields.  Returns people by Id, with Father and Mother as Ids.  (getAncestors is deprecated.)
            - getProfile: key, fields.
            - fields such as Id,Name,FirstName,LastNameAtBirth,BirthDate,DeathDate,BirthLocation,DeathLocation,Father,Mother.
            Only public profiles.  Rate limited: a 429 means wait.  Cite the profile page https://www.wikitree.com/wiki/<WikiTree ID>; it is a compiled tree, so look for the records its profile cites.
            """,
            Parameters: new Dictionary<string, string> { ["appId"] = "Mycelia" }),

        new("wikidata", "Wikidata", ["www.wikidata.org", "query.wikidata.org"],
            """
            Structured data on notable people and places.  No key.
            - Search: https://www.wikidata.org/w/api.php?action=wbsearchentities&search=<name>&language=en&format=json&limit=5
            - One item: https://www.wikidata.org/wiki/Special:EntityData/<Q id>.json (large: prefer SPARQL for a few values)
            - SPARQL: https://query.wikidata.org/sparql?format=json&query=<URL-encoded query>
            - Properties: P22 father, P25 mother, P26 spouse, P40 child, P569 date of birth, P570 date of death, P19 place of birth, P20 place of death.
            Only notable people (nobility, public figures).  Cite https://www.wikidata.org/wiki/<Q id>.
            """),

        new("openarchives", "Open Archives (Open Archieven)", ["api.openarch.nl", "api.openarchieven.nl", "www.openarchieven.nl"],
            """
            Indexed records of many archives, mainly the Netherlands and Belgium.  No key; at most 4 requests a second.
            - Search: https://api.openarch.nl/1.1/records/search.json?name=<names>&eventplace=<place>&eventtype=<type>&number_show=10&start=0&lang=en
              eventtype as the archive names it (Dutch, as in each result's _eventtype, such as Doop for baptism).  Each result has archive_code, identifier, personname, relationtype, eventtype, eventdate, eventplace, sourcetype and url.
            - One record: https://api.openarch.nl/1.1/records/show.json?archive=<archive_code>&identifier=<identifier>&lang=en, or as Markdown: https://www.openarchieven.nl/<archive_code>:<identifier>/md
            Cite the result's url.
            """),

        new("tna", "The National Archives (UK) Discovery", ["discovery.nationalarchives.gov.uk"],
            """
            The catalogue of The National Archives and over 2,500 other UK archives.  No key.  Mycelia asks for JSON.
            - Search: https://discovery.nationalarchives.gov.uk/API/search/records?sps.searchQuery=<words>&sps.dateFrom=YYYY-MM-DD&sps.dateTo=YYYY-MM-DD&sps.recordSeries=<series, such as PROB 11>&sps.resultsPageSize=20&sps.page=1
              Each record has id, reference (such as PROB 11/2217/406), description, coveringDates and heldBy.
            - One record: https://discovery.nationalarchives.gov.uk/API/records/v1/details/<id>
            It searches catalogue descriptions, not the text of documents.  At most one request a second and 3,000 a day.  Its terms ask that results are not cached or stored: cite and summarise, don't copy whole results into the tree.  Cite https://discovery.nationalarchives.gov.uk/details/r/<id>, the catalogue page, which is for the person to open (it doesn't give a program its text).
            """,
            Headers: new Dictionary<string, string> { ["Accept"] = "application/json" }),

        new("trove", "Trove (National Library of Australia)", ["api.trove.nla.gov.au"],
            """
            Australian newspapers, gazettes, books and pictures.  Needs a key, which Mycelia adds itself from Options › Research.
            - Search: https://api.trove.nla.gov.au/v3/result?q=<words>&category=newspaper&encoding=json&n=20
              encoding=json is needed (the default is XML).
            Cite the article's own Trove address from the result.
            """,
            KeyPlace.Header, "X-API-KEY", "API key", "https://trove.nla.gov.au/about/create-something/using-api"),

        new("digitalnz", "DigitalNZ (with Papers Past)", ["api.digitalnz.org"],
            """
            New Zealand collections, including Papers Past newspapers.  No key.
            - Search: https://api.digitalnz.org/v3/records.json?text=<words>&per_page=20&page=1&fields=id,title,date,landing_url
              Narrow with and[category][]=Newspapers (Papers Past and others).
            Papers Past data is for non-commercial use.  Cite the record's landing_url.
            """),

        new("europeana", "Europeana", ["api.europeana.eu"],
            """
            Digitised cultural heritage from European archives, libraries and museums: photographs, newspapers, documents.  Needs a key, which Mycelia adds itself from Options › Research.
            - Search: https://api.europeana.eu/record/v2/search.json?query=<words>&rows=20
            Cite the item's Europeana page from the result.
            """,
            KeyPlace.Header, "X-Api-Key", "API key", "https://www.europeana.eu/account/api-keys"),

        new("geonames", "GeoNames", ["secure.geonames.org"],
            """
            Modern place names with coordinates, for update_place.  Needs a GeoNames user name, which Mycelia adds itself from Options › Research.
            - Search: https://secure.geonames.org/searchJSON?q=<place>&country=<two-letter code, such as GB>&featureClass=P&maxRows=10
              featureClass=P is towns and villages.  Each result has name, adminName1, countryName, lat and lng.
            Modern places only: historical parishes and jurisdictions may differ.
            """,
            KeyPlace.Parameter, "username", "user name", "https://www.geonames.org/login"),

        // Downloads redirect to the Archive's own data servers (such as dn790006.ca.archive.org).
        new("internetarchive", "Internet Archive", ["archive.org", ".archive.org"],
            """
            Digitised books: local histories, directories, published family histories.  No key.
            - Search: https://archive.org/advancedsearch.php?q=<query, such as title:(Hebden) AND mediatype:texts>&fl[]=identifier&fl[]=title&fl[]=year&rows=20&page=1&output=json
            - An item's files: https://archive.org/metadata/<identifier>; a book's recognised text is usually the file ending _djvu.txt, at https://archive.org/download/<identifier>/<file name>
            Cite https://archive.org/details/<identifier>, with the page.
            """),
    ];

    /// <summary>The API serving this address, or null.</summary>
    public static ResearchApi? For(Uri uri) => All.FirstOrDefault(a => a.Serves(uri));

    public static ResearchApi? Get(string id) => All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checked and left out on 11 November 2026, with the reason (TDD 25.4).</summary>
    public static readonly IReadOnlyList<(string Name, string Reason)> LeftOut =
    [
        ("Library of Congress (with Chronicling America)", "its API is behind a bot check that a program can't pass"),
        ("NARA National Archives Catalog", "its API needs a key requested by email and gives only its web page without one"),
        ("GOV historic gazetteer", "its web service takes SOAP requests only, which fetch_url doesn't send"),
    ];
}
