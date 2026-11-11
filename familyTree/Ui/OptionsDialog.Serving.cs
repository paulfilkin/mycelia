using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mycelia.Ai;
using Mycelia.Ai.Server;

namespace Mycelia.Ui;

/// <summary>
/// Options › Other programs (TDD 24): let programs on this computer, such as Claude Desktop and Claude Code, use Family
/// Tree's tools on the open tree; the port, the token, who is connected, and ready-made settings to paste into them.
/// </summary>
public sealed partial class OptionsDialog
{
    private readonly CheckBox _serveEnabled = new() { Content = "Let other programs on this computer use Mycelia's tools" };
    private readonly TextBox _servePort = new() { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _serveToken = new() { IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
    private readonly TextBlock _serveNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private string _token = "";

    /// <summary>The programs connected now, by name; set by the main window.</summary>
    public Func<IReadOnlyList<string>>? ConnectedPrograms { get; set; }

    private void AddServingTab(TabControl tabs)
    {
        var s = _settings.McpServer;
        _serveEnabled.IsChecked = s.Enabled;
        _servePort.Text = s.Port.ToString();
        _token = SecretStore.Unprotect(s.Token);
        if (_token.Length == 0) _token = McpServerSettings.NewToken();
        _serveToken.Text = _token;
        _serveNote.Foreground = UiKit.Brush("Muted");

        // Who is connected is known once the window is shown (the main window sets ConnectedPrograms after making it).
        Loaded += (_, _) =>
        {
            var connected = ConnectedPrograms?.Invoke() ?? [];
            _serveNote.Text = !s.Enabled ? "Off.  Nothing can connect."
                : connected.Count == 0 ? $"On, at http://127.0.0.1:{s.Port}{McpServer.EndpointPath}.  No program is connected now."
                : $"On.  Connected now: {string.Join(", ", connected)}.";
        };

        var tokenRow = new DockPanel();
        var tokenButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        tokenButtons.Children.Add(SmallButton("Copy", (_, _) => CopyText(_token, "Copied the token.")));
        tokenButtons.Children.Add(SmallButton("New token", (_, _) =>
        {
            _token = McpServerSettings.NewToken();
            _serveToken.Text = _token;
            _serveNote.Text = "A new token: programs set up with the old one must be given this one.  OK keeps it.";
        }));
        DockPanel.SetDock(tokenButtons, Dock.Right);
        tokenRow.Children.Add(tokenButtons);
        tokenRow.Children.Add(_serveToken);

        var copyRow = new WrapPanel();
        copyRow.Children.Add(SmallButton("Copy for Claude Desktop", (_, _) => CopyText(ClaudeDesktopConfig(),
            "Copied.  In Claude Desktop: Settings › Developer › Edit Config, put this inside \"mcpServers\", save and restart Claude Desktop.")));
        copyRow.Children.Add(SmallButton("Copy for Claude Code", (_, _) => CopyText(ClaudeCodeCommand(),
            "Copied.  Run it in a terminal; Claude Code then lists mycelia under /mcp.")));

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("Other programs",
            "Programs on this computer that speak MCP, such as Claude Desktop and Claude Code, can use the same tools as the Assistant on the tree you have open.  Their changes ask you here first, every time, and each is one step you can undo.  Living and Private people are given to them as Options › Assistant says.  Nothing outside this computer can connect."));
        panel.Children.Add(Row(string.Empty, _serveEnabled));
        panel.Children.Add(Row("Port", _servePort, "The local port Mycelia listens on (1024 to 65535)."));
        panel.Children.Add(Row("Token", tokenRow, "Every request must carry it.  Keep it to yourself; New token shuts out programs given the old one."));
        panel.Children.Add(Row("Set up a program", copyRow,
            "Claude Desktop starts Mycelia's relay, which passes its messages on, so Mycelia must be running.  Claude Code connects directly."));
        panel.Children.Add(Row("Now", _serveNote));
        tabs.Items.Add(new TabItem { Header = "Other programs", Content = Scroll(panel) });
    }

    private int PortOrDefault() =>
        int.TryParse(_servePort.Text.Trim(), out int port) && port is >= 1024 and <= 65535 ? port : McpServerSettings.DefaultPort;

    /// <summary>The entry for claude_desktop_config.json's "mcpServers": this program started with --mcp-relay.</summary>
    private string ClaudeDesktopConfig()
    {
        var exe = Environment.ProcessPath ?? "Mycelia.exe";
        var entry = new JsonObject { ["mycelia"] = new JsonObject { ["command"] = exe, ["args"] = new JsonArray("--mcp-relay") } };
        var json = entry.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        // Just the entry, without the outer braces, ready to go inside "mcpServers".
        return json.Trim().TrimStart('{').TrimEnd('}').Trim();
    }

    private string ClaudeCodeCommand() =>
        $"claude mcp add --transport http mycelia http://127.0.0.1:{PortOrDefault()}{McpServer.EndpointPath} --header \"Authorization: Bearer {_token}\"";

    private void CopyText(string text, string note)
    {
        try
        {
            Clipboard.SetText(text);
            _serveNote.Text = note + (_settings.McpServer.Enabled && _serveEnabled.IsChecked == true ? "" : "  Tick the box above and press OK first.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _serveNote.Text = "The clipboard is busy; try again.";
        }
    }

    /// <summary>Writes the tab into the settings; false (with the reason shown) when the port isn't a usable number.</summary>
    private bool ApplyServing()
    {
        if (!int.TryParse(_servePort.Text.Trim(), out int port) || port is < 1024 or > 65535)
        {
            _serveNote.Text = "The port must be a number from 1024 to 65535.";
            return false;
        }
        var s = _settings.McpServer;
        s.Enabled = _serveEnabled.IsChecked == true;
        s.Port = port;
        s.Token = SecretStore.Protect(_token);
        return true;
    }
}
