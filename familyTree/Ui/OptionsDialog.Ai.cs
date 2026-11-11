using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mycelia.Ai;
using Mycelia.Ai.Assistant;
using Mycelia.Ai.Mcp;
using Microsoft.Win32;

namespace Mycelia.Ui;

/// <summary>
/// Options › AI model, Assistant, MCP servers, Built-in tools and Activity log (TDD 22.7).  Ported from
/// multifariousStudio's OptionsWindow pages without browser sign-in, the MCP Helper, AI Bridge and the round trip.
/// Everything is edited on copies and written to the settings by OK.
/// </summary>
public sealed partial class OptionsDialog
{
    private readonly McpHub? _hub;
    private readonly IReadOnlyList<AssistantTool> _builtInTools;

    // AI model
    private ComboBox _provider = null!;
    private ComboBox _model = null!;
    private ComboBox _endpoint = null!;
    private PasswordBox _apiKey = null!;
    private TextBlock _keyNote = null!;
    private TextBox _maxTokens = null!;
    private TextBlock _modelTestResult = null!;
    private readonly Dictionary<ModelProvider, string> _keys = [];

    // Assistant
    private TextBox _instructions = null!;
    private TextBox _maxSteps = null!;
    private CheckBox _connectOnStart = null!;
    private CheckBox _skipApprovals = null!;
    private ComboBox _livingPeople = null!;
    private CheckBox _includePrivate = null!;
    private Dictionary<string, ToolPolicy> _builtInPolicies = [];

    // MCP servers
    private List<McpServerConfig> _servers = [];
    private readonly Dictionary<string, List<McpToolInfo>> _knownTools = new(StringComparer.OrdinalIgnoreCase);
    private ListBox _serverList = null!;
    private FrameworkElement _serverEditor = null!;
    private McpServerConfig? _editing;
    private bool _loadingServer;
    private TextBox _serverName = null!;
    private CheckBox _serverEnabled = null!;
    private ComboBox _transport = null!;
    private TextBox _command = null!;
    private TextBox _arguments = null!;
    private TextBox _workingFolder = null!;
    private TextBox _environment = null!;
    private TextBox _url = null!;
    private TextBox _headers = null!;
    private TextBox _timeout = null!;
    private ComboBox _defaultPolicy = null!;
    private CheckBox _allowReadOnly = null!;
    private StackPanel _stdioFields = null!;
    private StackPanel _httpFields = null!;
    private TextBlock _testResult = null!;
    private StackPanel _serverTools = null!;

    // Activity log
    private TextBox _log = null!;

    private static readonly ToolPolicy[] Policies = [ToolPolicy.Allow, ToolPolicy.Ask, ToolPolicy.Off];

    private void AddAiTabs(TabControl tabs)
    {
        var a = _settings.Assistant;
        foreach (var provider in Enum.GetValues<ModelProvider>())
            _keys[provider] = a.GetApiKey(provider);
        _builtInPolicies = new Dictionary<string, ToolPolicy>(a.BuiltInPolicies);
        _servers = _settings.McpServers.Select(s => s.Clone()).ToList();
        if (_hub is not null)
            foreach (var state in _hub.States.Where(s => s.Status == McpServerStatus.Connected))
                _knownTools[state.Config.Id] = state.Tools.ToList();

        tabs.Items.Add(new TabItem { Header = "AI model", Content = Scroll(BuildModelPage()) });
        tabs.Items.Add(new TabItem { Header = "Assistant", Content = Scroll(BuildAssistantPage()) });
        tabs.Items.Add(new TabItem { Header = "MCP servers", Content = BuildServersPage() });
        tabs.Items.Add(new TabItem { Header = "Built-in tools", Content = BuildBuiltInPage() });
        var logTab = new TabItem { Header = "Activity log", Content = BuildLogPage() };
        tabs.Items.Add(logTab);
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedItem == logTab) RefreshLog(); };
    }

    // ---- Small helpers ----

    private static ScrollViewer Scroll(FrameworkElement content) =>
        new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };

    /// <summary>A labelled row: the label on the left, the control and an optional note on the right.</summary>
    private static FrameworkElement Row(string label, UIElement control, string? note = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, Foreground = UiKit.Brush("Muted"), FontSize = 12.5, Margin = new Thickness(0, 6, 10, 0), TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(text);
        var right = new StackPanel();
        right.Children.Add(control);
        if (note is not null) right.Children.Add(Hint(note));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiKit.Brush("Muted"),
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static TextBox Box(string text = "", bool multiLine = false, double height = 0)
    {
        var box = new TextBox { Text = text };
        if (multiLine)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.NoWrap;
            box.VerticalContentAlignment = VerticalAlignment.Top;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.Height = height;
        }
        return box;
    }

    private static Button SmallButton(string text, RoutedEventHandler click)
    {
        var button = UiKit.Button("SecondaryButton", text, click);
        button.Padding = new Thickness(10, 3, 10, 3);
        button.Margin = new Thickness(0, 0, 6, 4);
        return button;
    }

    private static FrameworkElement Heading(string title, string note)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(UiKit.Text(title, "H3"));
        var hint = Hint(note);
        panel.Children.Add(hint);
        return panel;
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);

    private static Dictionary<string, string> Pairs(string text, char separator)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            int at = line.IndexOf(separator);
            if (at <= 0)
                continue;
            result[line[..at].Trim()] = SecretStore.Protect(line[(at + 1)..].Trim());
        }
        return result;
    }

    // ---- AI model ----

    private FrameworkElement BuildModelPage()
    {
        var a = _settings.Assistant;
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("AI model",
            "The model the Assistant talks to.  It reads your request and the tools' results, and decides which tools to use.  Your requests and the tree details the tools read are sent to this service; a service on this computer (Ollama, LM Studio) keeps them here."));

        _provider = new ComboBox { ItemsSource = new[] { "Anthropic (Claude)", "OpenAI", "OpenAI-compatible (Ollama, LM Studio, Gemini, Mistral...)" }, SelectedIndex = (int)a.Provider, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        _model = new ComboBox { IsEditable = true, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Text = a.Model };
        _endpoint = new ComboBox { IsEditable = true, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Text = a.Endpoint, ItemsSource = ChatProviders.SuggestedEndpoints };
        _apiKey = new PasswordBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Password = _keys[a.Provider] };
        _keyNote = Hint(string.Empty);
        _maxTokens = Box(a.MaxTokens.ToString());
        _maxTokens.Width = 100;
        _maxTokens.HorizontalAlignment = HorizontalAlignment.Left;

        var current = a.Provider;
        _model.ItemsSource = ChatProviders.SuggestedModels(current);
        _provider.SelectionChanged += (_, _) =>
        {
            _keys[current] = _apiKey.Password;
            current = SelectedProvider;
            _model.ItemsSource = ChatProviders.SuggestedModels(current);
            if (!ChatProviders.SuggestedModels(current).Contains(_model.Text))
                _model.Text = ChatProviders.DefaultModel(current);
            _apiKey.Password = _keys[current];
            UpdateKeyNote();
        };
        _apiKey.PasswordChanged += (_, _) => { _keys[current] = _apiKey.Password; UpdateKeyNote(); };
        _endpoint.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateKeyNote()));

        panel.Children.Add(Row("Provider", _provider));
        panel.Children.Add(Row("Model", _model, "Pick one or type the model name the service uses."));
        panel.Children.Add(Row("Address", _endpoint, "Leave empty for Anthropic's or OpenAI's own address.  For an OpenAI-compatible service, its base address (ending in /v1 or /openai)."));
        var keyPanel = new StackPanel();
        keyPanel.Children.Add(_apiKey);
        keyPanel.Children.Add(_keyNote);
        panel.Children.Add(Row("API key", keyPanel));
        panel.Children.Add(Row("Longest answer", _maxTokens, "Tokens per answer (256 to 64000)."));

        var test = SmallButton("Test the model", async (_, _) => await TestModelAsync());
        test.HorizontalAlignment = HorizontalAlignment.Left;
        _modelTestResult = Hint(string.Empty);
        var testPanel = new StackPanel();
        testPanel.Children.Add(test);
        testPanel.Children.Add(_modelTestResult);
        panel.Children.Add(Row(string.Empty, testPanel));
        UpdateKeyNote();
        return panel;
    }

    private ModelProvider SelectedProvider => (ModelProvider)Math.Max(0, _provider.SelectedIndex);

    private void UpdateKeyNote()
    {
        bool needed = ChatProviders.NeedsKey(SelectedProvider, _endpoint.Text);
        _keyNote.Text = _apiKey.Password.Length > 0
            ? "Kept encrypted for your Windows account (DPAPI) in settings.json."
            : needed ? "Needed.  Keys are kept encrypted for your Windows account." : "Not needed for a service on this computer.";
    }

    private async Task TestModelAsync()
    {
        _modelTestResult.Text = "Asking the model…";
        try
        {
            var provider = ChatProviders.Create(new ChatProviderOptions(SelectedProvider, _model.Text.Trim(), _apiKey.Password.Trim(), _endpoint.Text.Trim(), 64));
            var reply = await provider.CompleteAsync("Reply with the single word OK.", new ChatTurn[] { new UserTurn("Test") }, Array.Empty<ToolSpec>(), CancellationToken.None);
            _modelTestResult.Text = $"It answered: {reply.Text.Trim()}  ({reply.InputTokens} tokens in, {reply.OutputTokens} out)";
        }
        catch (ChatException ex)
        {
            _modelTestResult.Text = ex.Message;
        }
    }

    // ---- Assistant ----

    private FrameworkElement BuildAssistantPage()
    {
        var a = _settings.Assistant;
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("Assistant",
            $"The conversation about each tree is kept until you clear it, encrypted for your Windows account, in {ConversationStore.Folder}.  A tree that has never been saved keeps its conversation only while it is open."));
        _instructions = Box(a.Instructions, multiLine: true, height: 130);
        _instructions.TextWrapping = TextWrapping.Wrap;
        _instructions.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _maxSteps = Box(a.MaxSteps.ToString());
        _maxSteps.Width = 100;
        _maxSteps.HorizontalAlignment = HorizontalAlignment.Left;
        _connectOnStart = new CheckBox { Content = "Connect to the MCP servers when Mycelia starts", IsChecked = a.ConnectOnStart };
        _skipApprovals = new CheckBox { Content = "Never ask before a tool runs (every Ask counts as Allow)", IsChecked = a.SkipApprovals };

        panel.Children.Add(Row("Your instructions", _instructions, "Added to every conversation, for example: \"My research is on the Ashworth family of Hebden Bridge.  Cite every record you find.\""));
        panel.Children.Add(Row("Steps per request", _maxSteps, "The most rounds of tool use for one request (1 to 100).  Each round is one request to the model."));
        panel.Children.Add(Row("Start-up", _connectOnStart));
        panel.Children.Add(Row("Approvals", _skipApprovals, "Off by default: tools that change the tree, and tools from MCP servers, ask first.  Choose per tool under MCP servers and Built-in tools."));

        // What the tree tools give the model about living people (TDD 22.4); the order is LivingPeopleDetail's.
        _livingPeople = new ComboBox
        {
            ItemsSource = new[] { "Names and relationships only", "Full details", "Not at all (an id only)" },
            SelectedIndex = (int)a.LivingPeople,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _includePrivate = new CheckBox { Content = "Include people and facts marked Private", IsChecked = a.IncludePrivate };
        panel.Children.Add(Row("Living people sent to the AI model", _livingPeople,
            $"Living means no death recorded, born within the last {Model.Living.BirthYearsLimit} years and married within the last {Model.Living.MarriageYearsLimit}.  With names and relationships only, their dates, places, facts, notes and media are withheld; you can still ask the Assistant to add what you tell it.  A service on this computer (Ollama, LM Studio) keeps everything here, so full details may suit it."));
        panel.Children.Add(Row("Private", _includePrivate, "Off by default: people and facts marked Private are withheld as living people are."));
        return panel;
    }

    // ---- MCP servers ----

    private FrameworkElement BuildServersPage()
    {
        var page = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };
        var top = Heading("MCP servers",
            "Model Context Protocol servers add tools the Assistant can use: record archives, catalogues, collaborative trees, gazetteers.  A server is a program on this computer (command) or a service at a web address (HTTP).");
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);

        // The list of servers and its buttons
        var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 16, 0) };
        var listButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var helper = UiKit.Button("PrimaryButton", "MCP Helper...", (_, _) => ShowHelper());
        helper.Padding = new Thickness(10, 3, 10, 3);
        helper.Margin = new Thickness(0, 0, 6, 4);
        helper.ToolTip = "Set up a genealogy archive or any MCP server, step by step";
        listButtons.Children.Add(helper);
        listButtons.Children.Add(SmallButton("Add", (_, _) => AddServer(new McpServerConfig { Name = "New server" })));
        listButtons.Children.Add(SmallButton("Import...", (_, _) => ImportServers()));
        listButtons.Children.Add(SmallButton("Remove", (_, _) => RemoveServer()));
        DockPanel.SetDock(listButtons, Dock.Top);
        left.Children.Add(listButtons);
        var hint = Hint("The MCP Helper sets up an archive's server for you.  Import reads the servers you set up for Claude Desktop, Claude Code, Cursor or VS Code (their mcpServers list).");
        hint.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(hint, Dock.Bottom);
        left.Children.Add(hint);
        _serverList = new ListBox { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1) };
        _serverList.SelectionChanged += (_, _) =>
        {
            CommitServerEditor();
            ShowServerEditor((_serverList.SelectedItem as ListBoxItem)?.Tag as McpServerConfig);
        };
        left.Children.Add(_serverList);
        DockPanel.SetDock(left, Dock.Left);
        page.Children.Add(left);

        // The selected server
        var editor = new StackPanel();
        _serverName = Box();
        _serverEnabled = new CheckBox { Content = "On (connect and offer its tools)" };
        _transport = new ComboBox { ItemsSource = new[] { "Command on this computer (stdio)", "Web address (Streamable HTTP)" }, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        _command = Box();
        _arguments = Box(multiLine: true, height: 66);
        _workingFolder = Box();
        _environment = Box(multiLine: true, height: 54);
        _url = Box();
        _headers = Box(multiLine: true, height: 54);
        _timeout = Box();
        _timeout.Width = 80;
        _timeout.HorizontalAlignment = HorizontalAlignment.Left;
        _defaultPolicy = new ComboBox { ItemsSource = Policies, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        _allowReadOnly = new CheckBox { Content = "Allow tools the server marks read-only without asking" };

        editor.Children.Add(Row("Name", _serverName));
        editor.Children.Add(Row(string.Empty, _serverEnabled));
        editor.Children.Add(Row("Connects by", _transport));
        _stdioFields = new StackPanel();
        _stdioFields.Children.Add(Row("Command", _command, "For example npx, uvx, node, python, or a program's full path."));
        _stdioFields.Children.Add(Row("Arguments", _arguments, "One per line, for example -y and the package name."));
        _stdioFields.Children.Add(Row("Start in folder", _workingFolder));
        _stdioFields.Children.Add(Row("Environment", _environment, "NAME=value, one per line, for API keys the server needs.  Values are kept encrypted."));
        _httpFields = new StackPanel();
        _httpFields.Children.Add(Row("Address", _url, "https://... (or http://localhost for this computer)."));
        _httpFields.Children.Add(BuildBrowserSignIn());
        _httpFields.Children.Add(Row("Header sign-in", BuildSignInHelper(), "For servers that take a key: makes the Authorization header from a user name and token (Basic) or a token (Bearer).  Or type the header below."));
        _httpFields.Children.Add(Row("Headers", _headers, "Name: value, one per line.  Values are kept encrypted."));
        editor.Children.Add(_stdioFields);
        editor.Children.Add(_httpFields);
        editor.Children.Add(Row("Wait for answers", _timeout, "Seconds."));
        editor.Children.Add(Row("New tools", _defaultPolicy, "Allow: used freely.  Ask: you approve each use.  Off: not offered to the model."));
        editor.Children.Add(Row(string.Empty, _allowReadOnly));

        var testRow = new WrapPanel();
        testRow.Children.Add(SmallButton("Test connection", async (_, _) => await TestServerAsync()));
        testRow.Children.Add(SmallButton("Allow all", (_, _) => SetAllPolicies(ToolPolicy.Allow)));
        testRow.Children.Add(SmallButton("Ask for all", (_, _) => SetAllPolicies(ToolPolicy.Ask)));
        _testResult = Hint(string.Empty);
        var testPanel = new StackPanel();
        testPanel.Children.Add(testRow);
        testPanel.Children.Add(_testResult);
        editor.Children.Add(Row(string.Empty, testPanel));
        editor.Children.Add(new TextBlock { Text = "Tools", Style = UiKit.Res<Style>("SectionLabel"), Margin = new Thickness(0, 4, 0, 6) });
        _serverTools = new StackPanel();
        editor.Children.Add(_serverTools);

        foreach (var box in new[] { _serverName, _command, _arguments, _workingFolder, _environment, _url, _headers, _timeout })
            box.TextChanged += (_, _) => CommitServerEditor();
        _serverEnabled.Click += (_, _) => CommitServerEditor();
        _allowReadOnly.Click += (_, _) => CommitServerEditor();
        _defaultPolicy.SelectionChanged += (_, _) => CommitServerEditor();
        _transport.SelectionChanged += (_, _) =>
        {
            bool http = _transport.SelectedIndex == 1;
            _stdioFields.Visibility = http ? Visibility.Collapsed : Visibility.Visible;
            _httpFields.Visibility = http ? Visibility.Visible : Visibility.Collapsed;
            CommitServerEditor();
        };

        _serverEditor = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = editor, Padding = new Thickness(0, 0, 8, 0) };
        page.Children.Add(_serverEditor);

        foreach (var server in _servers) _serverList.Items.Add(ServerItem(server));
        if (_serverList.Items.Count > 0) _serverList.SelectedIndex = 0;
        else ShowServerEditor(null);
        return page;
    }

    private string StatusOf(McpServerConfig config)
    {
        if (!config.Enabled) return "Off";
        return _hub?.State(config.Id)?.StatusText ?? "Not connected yet (OK connects)";
    }

    private ListBoxItem ServerItem(McpServerConfig config)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
        panel.Children.Add(new TextBlock { Text = config.Name.Length > 0 ? config.Name : config.Id, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock
        {
            Text = (config.Transport == McpTransportKind.Http ? "Web address" : "Command") + " · " + StatusOf(config),
            FontSize = 11.5,
            Foreground = UiKit.Brush("Muted"),
            TextWrapping = TextWrapping.Wrap,
        });
        return new ListBoxItem { Content = panel, Tag = config };
    }

    /// <summary>The content of a freshly made item, taken out of it so it can go into another.</summary>
    private static object DetachContent(ListBoxItem item)
    {
        var content = item.Content;
        item.Content = null;
        return content;
    }

    /// <summary>Sign-in for a server that takes an Authorization header: Basic from a user name and token, or Bearer from a token.</summary>
    private FrameworkElement BuildSignInHelper()
    {
        var kind = new ComboBox { ItemsSource = new[] { "Basic: user name and token", "Bearer: token" }, SelectedIndex = 1, Width = 190, Margin = new Thickness(0, 0, 6, 4) };
        var user = Box();
        user.Width = 130;
        user.Margin = new Thickness(0, 0, 6, 4);
        user.ToolTip = "User name";
        user.Visibility = Visibility.Collapsed;
        var secret = new PasswordBox { Width = 150, Margin = new Thickness(0, 0, 6, 4), ToolTip = "Token or password" };
        kind.SelectionChanged += (_, _) => user.Visibility = kind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        var apply = SmallButton("Set header", (_, _) =>
        {
            if (secret.Password.Length == 0 || (kind.SelectedIndex == 0 && user.Text.Trim().Length == 0))
            {
                _testResult.Text = kind.SelectedIndex == 0 ? "Type the user name and the token." : "Type the token.";
                return;
            }
            string value = kind.SelectedIndex == 0
                ? "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user.Text.Trim() + ":" + secret.Password))
                : "Bearer " + secret.Password.Trim();
            var lines = Lines(_headers.Text).Where(l => !l.TrimStart().StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)).ToList();
            lines.Insert(0, "Authorization: " + value);
            _headers.Text = string.Join(Environment.NewLine, lines);
            secret.Clear();
            _testResult.Text = "The Authorization header is set.  Test the connection to try it.";
        });
        var row = new WrapPanel();
        row.Children.Add(kind);
        row.Children.Add(user);
        row.Children.Add(secret);
        row.Children.Add(apply);
        return row;
    }

    private void ShowServerEditor(McpServerConfig? config)
    {
        _editing = config;
        _serverEditor.IsEnabled = config is not null;
        _serverEditor.Opacity = config is not null ? 1 : 0.5;
        _loadingServer = true;
        try
        {
            var c = config ?? new McpServerConfig();
            _serverName.Text = c.Name;
            _serverEnabled.IsChecked = c.Enabled;
            _transport.SelectedIndex = c.Transport == McpTransportKind.Http ? 1 : 0;
            _stdioFields.Visibility = c.Transport == McpTransportKind.Http ? Visibility.Collapsed : Visibility.Visible;
            _httpFields.Visibility = c.Transport == McpTransportKind.Http ? Visibility.Visible : Visibility.Collapsed;
            _command.Text = c.Command;
            _arguments.Text = string.Join(Environment.NewLine, c.Arguments);
            _workingFolder.Text = c.WorkingDirectory ?? string.Empty;
            _environment.Text = string.Join(Environment.NewLine, c.Environment.Select(p => $"{p.Key}={SecretStore.Unprotect(p.Value)}"));
            _url.Text = c.Url;
            _headers.Text = string.Join(Environment.NewLine, c.Headers.Select(p => $"{p.Key}: {SecretStore.Unprotect(p.Value)}"));
            _timeout.Text = c.TimeoutSeconds.ToString();
            _defaultPolicy.SelectedItem = c.DefaultPolicy;
            _allowReadOnly.IsChecked = c.AllowReadOnlyTools;
            ShowOAuth(c);
            _testResult.Text = config is null
                ? "Add a server, or import the ones you set up for Claude Desktop, Claude Code, Cursor or VS Code."
                : _hub?.State(c.Id)?.ServerDescription ?? string.Empty;
        }
        finally
        {
            _loadingServer = false;
        }
        ShowServerTools();
    }

    private void ShowServerTools()
    {
        _serverTools.Children.Clear();
        if (_editing is not { } config || !_knownTools.TryGetValue(config.Id, out var tools))
        {
            _serverTools.Children.Add(Hint("Test the connection to list the server's tools and choose for each whether it is allowed, asked about or off."));
            return;
        }
        foreach (var tool in tools)
        {
            string name = tool.Name;
            _serverTools.Children.Add(ToolRow(tool.DisplayName, tool.Description, tool.ReadOnly ? "Reads" : tool.Destructive ? "Deletes" : "Changes",
                config.PolicyFor(tool), policy => config.ToolPolicies[name] = policy));
        }
    }

    /// <summary>One tool with what it does and a choice of Allow, Ask or Off.</summary>
    private static FrameworkElement ToolRow(string title, string description, string kind, ToolPolicy policy, Action<ToolPolicy> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var flat = description.ReplaceLineEndings(" ");
        text.Children.Add(new TextBlock { Text = flat, FontSize = 12, Foreground = UiKit.Brush("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = flat.Length > 0 ? flat : null });
        grid.Children.Add(text);
        var kindText = new TextBlock { Text = kind, FontSize = 12, Foreground = UiKit.Brush("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(kindText, 1);
        grid.Children.Add(kindText);
        var choice = new ComboBox { ItemsSource = Policies, SelectedItem = policy, VerticalAlignment = VerticalAlignment.Center };
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is ToolPolicy p) set(p); };
        Grid.SetColumn(choice, 2);
        grid.Children.Add(choice);
        return grid;
    }

    private void SetAllPolicies(ToolPolicy policy)
    {
        if (_editing is not { } config || !_knownTools.TryGetValue(config.Id, out var tools)) return;
        foreach (var tool in tools) config.ToolPolicies[tool.Name] = policy;
        ShowServerTools();
    }

    /// <summary>Writes the editor's fields into the server being edited (secret values encrypted).</summary>
    private void CommitServerEditor()
    {
        if (_loadingServer || _editing is not { } c)
            return;
        c.Name = _serverName.Text.Trim();
        c.Enabled = _serverEnabled.IsChecked == true;
        c.Transport = _transport.SelectedIndex == 1 ? McpTransportKind.Http : McpTransportKind.Stdio;
        c.Command = _command.Text.Trim();
        c.Arguments = Lines(_arguments.Text).ToList();
        c.WorkingDirectory = _workingFolder.Text.Trim().Length > 0 ? _workingFolder.Text.Trim() : null;
        c.Environment = Pairs(_environment.Text, '=');
        c.Url = _url.Text.Trim();
        c.Headers = Pairs(_headers.Text, ':');
        c.TimeoutSeconds = int.TryParse(_timeout.Text, out int seconds) ? Math.Clamp(seconds, 5, 3600) : 60;
        if (_defaultPolicy.SelectedItem is ToolPolicy policy)
            c.DefaultPolicy = policy;
        c.AllowReadOnlyTools = _allowReadOnly.IsChecked == true;
        CommitOAuth(c);
        foreach (ListBoxItem item in _serverList.Items)
            if (item.Tag == c) item.Content = DetachContent(ServerItem(c));
    }

    private void AddServer(McpServerConfig config)
    {
        CommitServerEditor();
        if (config.Id.Length == 0)
            config.Id = McpServerConfig.MakeId(config.Name, _servers.Select(s => s.Id));
        _servers.Add(config);
        var item = ServerItem(config);
        _serverList.Items.Add(item);
        _serverList.SelectedItem = item;
        _serverName.Focus();
        _serverName.SelectAll();
    }

    /// <summary>
    /// The MCP Helper (TDD 22.9): the server it set up goes into the selected server when that one is still empty (a new
    /// server with no address or command), else into a new server.
    /// </summary>
    private void ShowHelper()
    {
        CommitServerEditor();
        var window = new McpHelperWindow(_settings.Assistant) { Owner = this };
        if (window.ShowDialog() != true || window.Plan is not { } plan)
            return;
        bool empty = _editing is { } current && current.Url.Trim().Length == 0 && current.Command.Trim().Length == 0;
        if (empty)
        {
            plan.FillIn(_editing!, window.Answers);
            foreach (ListBoxItem item in _serverList.Items)
                if (item.Tag == _editing) item.Content = DetachContent(ServerItem(_editing!));
            ShowServerEditor(_editing);
        }
        else
        {
            var server = new McpServerConfig { Name = plan.Name };
            plan.FillIn(server, window.Answers);
            AddServer(server);
        }
        _testResult.Text = plan.BrowserSignIn
            ? "Filled in by the MCP Helper.  Press Sign in under Browser sign-in, then Test connection; OK keeps it."
            : "Filled in by the MCP Helper.  Press Test connection to check it; OK keeps it.";
    }

    private void RemoveServer()
    {
        if (_serverList.SelectedItem is not ListBoxItem { Tag: McpServerConfig config } item)
            return;
        if (MessageBox.Show(this, $"Remove {config.Name}?", "Options", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _editing = null;
        _servers.Remove(config);
        _serverList.Items.Remove(item);
        if (_serverList.Items.Count > 0)
            _serverList.SelectedIndex = 0;
        else
            ShowServerEditor(null);
    }

    /// <summary>Reads servers from a Claude Desktop, Claude Code, Cursor or VS Code configuration file.</summary>
    private void ImportServers()
    {
        string claudeDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
        var dialog = new OpenFileDialog
        {
            Title = "Import MCP servers",
            Filter = "MCP configuration (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = File.Exists(claudeDesktop) ? Path.GetDirectoryName(claudeDesktop) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            FileName = File.Exists(claudeDesktop) ? "claude_desktop_config.json" : string.Empty
        };
        if (dialog.ShowDialog(this) != true)
            return;
        List<McpServerConfig> imported;
        try
        {
            imported = McpServerConfig.ImportFromJson(File.ReadAllText(dialog.FileName), _servers.Select(s => s.Id));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, "The file couldn't be read: " + ex.Message, "Options", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int added = 0;
        foreach (var server in imported)
        {
            if (_servers.Any(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            server.Environment = SecretStore.ProtectValues(server.Environment);
            server.Headers = SecretStore.ProtectValues(server.Headers);
            AddServer(server);
            added++;
        }
        MessageBox.Show(this, imported.Count == 0 ? "The file has no \"mcpServers\" section." : $"Imported {added} of {imported.Count} servers (servers with names already here were left out).",
            "Options", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task TestServerAsync()
    {
        CommitServerEditor();
        if (_editing is not { } config)
            return;
        _testResult.Text = "Connecting…";
        var lines = new List<string>();
        var probe = config.Clone();
        var (ok, message, tools) = await McpHub.TestAsync(probe, line => { lock (lines) lines.Add(line); _hub?.Log(line); }, CancellationToken.None);
        // A token refreshed (or forgotten) during the test belongs to the server being edited.
        if (probe.OAuth.Enabled && probe.OAuth.AccessToken != config.OAuth.AccessToken)
        {
            config.OAuth.TakeTokensFrom(probe.OAuth);
            ShowOAuthStatus(config);
        }
        _testResult.Text = message;
        if (ok)
        {
            _knownTools[config.Id] = tools;
            ShowServerTools();
        }
        else
        {
            string errors;
            lock (lines)
                errors = string.Join(Environment.NewLine, lines.Where(l => l.Contains("stderr", StringComparison.Ordinal)).TakeLast(4));
            if (errors.Length > 0)
                _testResult.Text += Environment.NewLine + errors;
        }
    }

    // ---- Built-in tools ----

    private FrameworkElement BuildBuiltInPage()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("Built-in tools",
            "Mycelia's own tools for the Assistant.  Tools that only read are allowed by default; tools that change the tree ask first, and every change is one step you can undo."));
        foreach (var tool in _builtInTools)
        {
            string name = tool.Name;
            var defaultPolicy = tool.Default;
            var policy = _builtInPolicies.TryGetValue(name, out var chosen) ? chosen : defaultPolicy;
            panel.Children.Add(ToolRow(tool.Title, tool.Description, tool.Changes ? "Changes" : tool.ReachesWeb ? "Web" : "Reads", policy, p =>
            {
                if (p == defaultPolicy) _builtInPolicies.Remove(name);
                else _builtInPolicies[name] = p;
            }));
        }
        return Scroll(panel);
    }

    // ---- Activity log ----

    private FrameworkElement BuildLogPage()
    {
        var panel = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };
        var top = new StackPanel();
        top.Children.Add(Heading("Activity log", "What the MCP servers said: connections, tool lists, calls, results and their error output (the last 1000 lines)."));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(SmallButton("Refresh", (_, _) => RefreshLog()));
        buttons.Children.Add(SmallButton("Copy", (_, _) => { try { Clipboard.SetText(_log.Text); } catch (System.Runtime.InteropServices.COMException) { } }));
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        panel.Children.Add(_log);
        return panel;
    }

    private void RefreshLog()
    {
        var lines = _hub?.LogLines ?? [];
        _log.Text = lines.Count == 0 ? "Nothing yet." : string.Join(Environment.NewLine, lines);
        _log.ScrollToEnd();
    }

    // ---- Saving ----

    /// <summary>Writes the Assistant's pages into the settings; false (and the page shown) when something must be fixed first.</summary>
    private bool ApplyAi()
    {
        CommitServerEditor();
        var a = _settings.Assistant;
        var provider = SelectedProvider;
        _keys[provider] = _apiKey.Password;
        a.Provider = provider;
        a.Model = _model.Text.Trim().Length > 0 ? _model.Text.Trim() : ChatProviders.DefaultModel(provider);
        a.Endpoint = _endpoint.Text.Trim();
        foreach (var (key, value) in _keys)
            a.SetApiKey(key, value);
        a.MaxTokens = int.TryParse(_maxTokens.Text, out int tokens) ? Math.Clamp(tokens, 256, 64000) : AssistantSettings.DefaultMaxTokens;
        a.MaxSteps = int.TryParse(_maxSteps.Text, out int steps) ? Math.Clamp(steps, 1, 100) : 20;
        a.Instructions = _instructions.Text;
        a.ConnectOnStart = _connectOnStart.IsChecked == true;
        a.SkipApprovals = _skipApprovals.IsChecked == true;
        a.LivingPeople = (LivingPeopleDetail)Math.Max(0, _livingPeople.SelectedIndex);
        a.IncludePrivate = _includePrivate.IsChecked == true;
        a.BuiltInPolicies = new Dictionary<string, ToolPolicy>(_builtInPolicies);
        foreach (var server in _servers.Where(s => s.Name.Length == 0))
            server.Name = server.Id;
        KeepSavedTokens();
        _settings.McpServers = _servers.Select(s => s.Clone()).ToList();
        return true;
    }
}
