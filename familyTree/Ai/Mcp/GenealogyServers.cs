namespace Mycelia.Ai.Mcp;

/// <summary>
/// One MCP server for family history research in the MCP Helper's genealogy list (TDD 22.9).  Offered entries are
/// set up from their address; the others are listed with the reason they aren't offered.  A package-based entry, if
/// one is ever offered, names an exact version, so an update to the package can't change what runs without a new
/// Mycelia release.
/// </summary>
public sealed record GenealogyServer(
    string Name,
    string Coverage,
    string RunBy,
    string Description,
    string? Url,
    bool Offered,
    string? Note = null,
    string? Reason = null,
    string? Package = null);

/// <summary>
/// The genealogy list, checked on 11 November 2026: each offered server answered an MCP initialise without a sign-in;
/// each repository and package was looked up.  No third-party server program was run.  Sources:
/// Docs/Resources/genealogy-resources.md and the checks recorded in TDD 22.9.
/// </summary>
public static class GenealogyServers
{
    public static readonly IReadOnlyList<GenealogyServer> All =
    [
        new("Open Archives (Open Archieven)", "Netherlands and Belgium",
            "Open Archives itself",
            "One search across the indexed records of many Dutch and Belgian archives: baptisms, marriages, burials, civil registration, population registers, notarial records, with links to the scans.",
            "https://mcp.openarchieven.nl/", true,
            Note: "Run by Open Archives itself; no sign-in.  The Assistant can also read its API without this server (fetch_url)."),

        new("Riksarkivet (Swedish National Archives)", "Sweden",
            "Riksarkivet itself (a beta)",
            "Searches Riksarkivet's records, including AI-transcribed court records; it returns only material older than 110 years.",
            "https://riksarkivet-ra-mcp.hf.space/mcp", true,
            Note: "A beta that Riksarkivet runs until the end of 2026; it may stop or change after that.  No sign-in."),

        new("Australian History (Trove, PROV and others)", "Australia", "a third party",
            "A community server for Trove and several Australian archives (npm @littlebearapps/australian-history-mcp 1.0.0).",
            null, false,
            Reason: "Its source code is no longer public (its GitHub repository is gone), so what it does with your searches can't be checked.  The Assistant reads Trove directly with your Trove key (Options › Research).",
            Package: "@littlebearapps/australian-history-mcp@1.0.0"),

        new("WikiTree", "Worldwide", "third parties",
            "Community servers for the WikiTree shared tree (PeWu/wikitree-mcp, Genealogy-MCP/wikitree-mcp).",
            null, false,
            Reason: "Not needed: the Assistant reads WikiTree's own API directly (fetch_url), with no extra program to run."),

        new("Wikidata", "Worldwide", "third parties",
            "Community servers for Wikidata (cyanheads/wikidata-mcp-server, zzaebok/mcp-wikidata).",
            null, false,
            Reason: "Not needed: the Assistant reads Wikidata directly (fetch_url)."),

        new("Europeana", "Europe", "a third party",
            "A community server for Europeana (pipeworx-io/mcp-europeana).",
            null, false,
            Reason: "Not needed: the Assistant reads Europeana's API directly with your Europeana key (fetch_url, Options › Research)."),

        new("FamilySearch", "Worldwide", "third parties",
            "Community servers for FamilySearch (JonathanReiss15/familysearch-mcp and others).",
            null, false,
            Reason: "FamilySearch grants its API only to approved partners, not for personal use, and some of these servers reuse your browser's sign-in instead, outside FamilySearch's terms.  Search FamilySearch on its website."),

        new("Arolsen Archives", "Europe", "a third party",
            "A community server for the Arolsen Archives (wspringer/arolsen-mcp).",
            null, false,
            Reason: "Built on the archive's undocumented addresses, and the archive's terms forbid reusing its material elsewhere without consent.  Search it on its website."),

        new("NARA (US National Archives)", "United States", "a third party",
            "A community server for the National Archives Catalog (GeorgeTownSabatical/nara-mcp-server).",
            null, false,
            Reason: "Its repository is gone.  NARA's own API needs a key requested by email."),
    ];
}
