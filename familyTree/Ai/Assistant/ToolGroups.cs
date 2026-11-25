namespace Mycelia.Ai.Assistant;

/// <summary>
/// The tools in groups, so a model on this computer is sent only the ones a request is likely to need (TDD 22.2): the
/// 68 built-in tools' definitions were about 10,250 of the 12,671 tokens of the first request with qwen3:8b on
/// 25 November 2026.  It gets the tools that are always sent, the groups for the workspace the person is on, and
/// use_tools to ask for another group; each connected MCP server is a group of its own.  Hosted models get every tool.
/// The groups and the use_tools tool follow HoboLibris's.
/// </summary>
public static class ToolGroups
{
    public const string People = "people";
    public const string History = "family_history";
    public const string Sources = "sources";
    public const string Places = "places";
    public const string Media = "media";
    public const string Research = "research";
    public const string WholeTree = "whole_tree";

    public const string UseToolsName = "use_tools";

    /// <summary>The built-in groups, with what each is for (for the model).</summary>
    public static readonly IReadOnlyList<(string Name, string Purpose)> BuiltIn =
    [
        (People, "add, change, link, merge and delete people, families and facts; fact types"),
        (History, "ancestors, descendants, relationships, timelines, charts, date calculations, Soundex, historical events"),
        (Sources, "read, add and change sources and repositories; add and remove citations"),
        (Places, "read, change and combine places"),
        (Media, "add and link media; download a record image or document"),
        (Research, "research context, the research log, notes, where to search (suggest_resources), reading archive APIs and web pages (fetch_url, api_notes)"),
        (WholeTree, "find and replace text, data checks, duplicates, converting names, GEDCOM export, merging a tree file, saving and backing up")
    ];

    // Tools not listed here (get_context, search_people, get_person, get_family, select_person, show_workspace,
    // read_help, parse_date, undo, and any new tool until it is given a group) are always sent.  So are the small tools
    // that say what the tree holds (list_sources, list_places, list_media, list_repositories, tree_statistics): on
    // 25 November 2026 qwen3:8b on People called list_places, which it wasn't sent, and Ollama dropped the call, leaving
    // an empty reply.
    // A group's tools are listed in the note sent after an empty reply (AssistantSession.EmptyReplyNote).
    private static readonly Dictionary<string, string> ByTool = new()
    {
        ["add_person"] = People, ["update_person"] = People, ["delete_person"] = People, ["add_fact"] = People,
        ["update_fact"] = People, ["delete_fact"] = People, ["link_spouse"] = People, ["link_child"] = People,
        ["link_parent"] = People, ["unlink"] = People, ["update_family"] = People, ["merge_people"] = People,
        ["combine_families"] = People, ["sort_children"] = People, ["set_home_person"] = People, ["set_bookmark"] = People,
        ["add_fact_type"] = People, ["list_fact_types"] = People,

        ["get_ancestors"] = History, ["get_descendants"] = History, ["get_relationship"] = History, ["get_timeline"] = History,
        ["show_chart"] = History, ["date_calculator"] = History, ["soundex"] = History,
        ["add_historical_event"] = History, ["list_historical_events"] = History,

        ["get_source"] = Sources, ["add_source"] = Sources, ["update_source"] = Sources,
        ["add_citation"] = Sources, ["remove_citation"] = Sources, ["add_repository"] = Sources,

        ["get_place"] = Places, ["update_place"] = Places, ["combine_places"] = Places,

        ["add_media"] = Media, ["link_media"] = Media, ["download_file"] = Media,

        ["research_context"] = Research, ["log_research"] = Research, ["add_note"] = Research,
        ["suggest_resources"] = Research, ["fetch_url"] = Research, ["api_notes"] = Research,

        ["find_text"] = WholeTree, ["replace_text"] = WholeTree, ["check_data_errors"] = WholeTree,
        ["find_duplicates"] = WholeTree, ["convert_names"] = WholeTree, ["export_gedcom"] = WholeTree,
        ["merge_tree_file"] = WholeTree, ["save_tree"] = WholeTree, ["backup_tree"] = WholeTree
    };

    /// <summary>A tool's group: its MCP server's, or the built-in one; null for a tool that is always sent.</summary>
    public static string? Of(AssistantTool tool) =>
        tool.ServerId is { } server ? ServerGroup(server) : ByTool.GetValueOrDefault(tool.Name);

    /// <summary>The group of an MCP server's tools.</summary>
    public static string ServerGroup(string serverId) => "mcp_" + serverId;

    /// <summary>The groups sent for a workspace, as the context names it.  People research its people, so People has the research tools too.</summary>
    public static IReadOnlyList<string> ForWorkspace(string? workspace) => workspace switch
    {
        "Places" => [Places],
        "Sources" => [Sources],
        "Media" => [Media],
        "Home" or "Charts" => [History],
        "Tools" => [WholeTree],
        _ => [People, Research]
    };
}
