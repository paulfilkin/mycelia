using System.Windows;
using System.Windows.Input;

namespace Mycelia.Ui;

/// <summary>
/// The help pages (Docs/Help in the repository), opened in the browser: F1 opens the page for the
/// screen in front, and the main menu's Help items list the rest (TDD 28).
/// </summary>
public static class Help
{
    /// <summary>Where the pages are read: the repository on GitHub, which shows Markdown as web pages.</summary>
    public const string BaseAddress = "https://github.com/paulfilkin/mycelia/blob/main/Docs/Help/";

    /// <summary>The issue forms (bug report, feature request).</summary>
    public const string IssuesAddress = "https://github.com/paulfilkin/mycelia/issues/new/choose";

    public const string Home = "README.md";
    public const string KeyboardShortcuts = "keyboard-shortcuts.md";
    public const string Assistant = "assistant.md";
    public const string McpHelper = "mcp-servers.md#the-mcp-helper";

    public static void Open(string page) => Dialogs.OpenWebAddress(BaseAddress + page);

    /// <summary>The page for a workspace, by MainWindow's names; "Person" is the People workspace's Person view.</summary>
    public static string ForWorkspace(string? workspace) => workspace switch
    {
        "People" => "people.md",
        "Person" => "people.md#the-person-view",
        "Places" => "places.md",
        "Sources" => "sources.md",
        "Media" => "media.md",
        "Charts" => "charts.md",
        "Tools" => "tools.md",
        _ => "getting-started.md",
    };

    /// <summary>The page for a tab of the Options window, by its header.</summary>
    public static string ForOptionsTab(string? tab) => tab switch
    {
        "General" => "options-and-updates.md#general",
        "Dates and names" => "options-and-updates.md#dates-and-names",
        "Warnings" => "options-and-updates.md#warnings",
        "Updates" => "options-and-updates.md#updates",
        "AI model" => "assistant.md#choosing-an-ai-model",
        "Assistant" => "assistant.md#what-the-ai-model-sees",
        "Built-in tools" => "assistant.md#built-in-tools",
        "Activity log" => "assistant.md#activity-log",
        "MCP servers" => "mcp-servers.md#adding-a-server-by-hand",
        "Research" => "mcp-servers.md#research-settings",
        "Other programs" => "other-programs.md",
        _ => "options-and-updates.md",
    };

    /// <summary>F1 in a window opens the page <paramref name="page"/> names at that moment.</summary>
    public static void BindF1(Window window, Func<string> page) =>
        window.InputBindings.Add(new KeyBinding(new RelayCommand(() => Open(page())), Key.F1, ModifierKeys.None));
}
