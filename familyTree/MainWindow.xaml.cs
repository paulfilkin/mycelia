using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Mycelia.Ai.Assistant;
using Mycelia.Ai.Mcp;
using Mycelia.Ai.Tools;
using Mycelia.Model;
using Mycelia.Ui;
using Microsoft.Win32;

namespace Mycelia;

public partial class MainWindow : Window, IAssistantHost, IToolHost, Updates.IUpdateHost, Ai.Server.IMcpServerHost
{
    private readonly AppSettings _settings;
    private readonly AppState _state;
    private readonly bool _screenshot;
    private readonly McpHub _hub = new();
    private readonly AssistantTools _tools;
    private readonly AssistantSession _assistant;
    private AssistantPane? _pane;

    /// <summary>The workspace on show ("People", "Places"...), for the Assistant's context.</summary>
    private string _workspace = "People";

    public MainWindow(string? screenshotPath, string? openPath)
    {
        InitializeComponent();
        _screenshot = screenshotPath is not null;
        _settings = _screenshot ? new AppSettings { ReadOnly = true } : AppSettings.Load();
        OptionsDialog.ApplyToApp(_settings);

        _state = new AppState(InitialDocument(openPath));
        _state.EditStateChanged += UpdateChrome;
        _state.DocumentReplaced += UpdateChrome;
        _state.TreeChanged += UpdateChrome;
        _state.NavigateRequested += Navigate;
        _state.NavigationChanged += UpdateNavigation;

        HomeView.Attach(_state, _settings, this);
        PeopleView.Attach(_state);
        PlacesView.Attach(_state);
        SourcesView.Attach(_state);
        MediaView.Attach(_state);
        ChartsView.Attach(_state);
        ToolsView.Attach(_state);

        // Foldable panels take their saved state (TDD 16.1); the Assistant's width follows the room left.
        FoldLayout.UseSettings(_settings);
        Body.SizeChanged += (_, _) => UpdateAssistantColumn();
        LayoutUpdated += (_, _) => AlignAssistant();
        SizeChanged += (_, _) => FitTopBar();
        foreach (var tab in NavTabs.Children.OfType<RadioButton>()) tab.ToolTip = (string)tab.Tag;

        // The Assistant (TDD 22): one conversation per tree, MCP servers connected in the background.
        _tools = new AssistantTools(_state, this);
        _assistant = new AssistantSession(this, _hub);
        _assistant.ApplyBuiltInPolicies();
        _assistant.OpenTree(_state.Doc.FilePath);
        _assistant.Status += text => StatusRight.Text = text;
        _state.DocumentReplaced += () => _assistant.OpenTree(_state.Doc.FilePath);
        _state.EditStateChanged += () => _assistant.TreeSaved(_state.Doc.FilePath);
        if (!_screenshot && _settings.Assistant.ConnectOnStart && _settings.McpServers.Count > 0)
            _ = _hub.ApplyAsync(_settings.McpServers, CancellationToken.None);
        if (!_screenshot && _settings.Assistant.PaneVisible) ShowAssistant();
        McpOAuth.TokensChanged += McpTokensChanged;

        // Other programs using the tools (TDD 24): approvals need the pane; never connect to ourselves.
        _assistant.ShowForApproval = ShowAssistantForApproval;
        _hub.Refuse = RefuseSelf;
        StartServer();

        // Software updates (TDD 23): a silent check once the window is on screen; never in screenshot mode.
        if (!_screenshot) Loaded += async (_, _) => await Updates.UpdateService.CheckAtStartupAsync(this);

        // Options > General: the start-up workspace (the screenshot always shows People).
        Navigate(_screenshot ? "People" : _settings.StartWorkspace);
        if (TabPeople.IsChecked != true && NavTabs.Children.OfType<RadioButton>().All(r => r.IsChecked != true)) TabPeople.IsChecked = true;
        UpdateChrome();
        UpdateNavigation();

        void Bind(Action action, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
            InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));
        Bind(() => OnSave(this, new RoutedEventArgs()), Key.S, ModifierKeys.Control);
        Bind(() => OnUndo(this, new RoutedEventArgs()), Key.Z, ModifierKeys.Control);
        Bind(() => OnRedo(this, new RoutedEventArgs()), Key.Y, ModifierKeys.Control);
        Bind(OpenFile, Key.O, ModifierKeys.Control);
        Bind(NewTree, Key.N, ModifierKeys.Control);

        // The familiar shortcuts (Design considerations, Keyboard shortcuts).
        Bind(_state.GoBack, Key.Left, ModifierKeys.Alt);
        Bind(_state.GoForward, Key.Right, ModifierKeys.Alt);
        Bind(GoHome, Key.Home, ModifierKeys.Control);
        Bind(ToggleBookmark, Key.B, ModifierKeys.Control | ModifierKeys.Shift);
        Bind(() => Dialogs.FindAndReplace(_state), Key.F, ModifierKeys.Control);
        Bind(() => Navigate("Person"), Key.F3);
        Bind(() => Navigate("Tools"), Key.D, ModifierKeys.Control);
        // F1: the help page for what is in front (Help, TDD 28).
        Bind(() => Help.Open(CurrentHelpPage()), Key.F1);
        // Ctrl+1 to Ctrl+7 in the familiar workspace order: Home, People, Places, Media, Sources, Charts, then Tools.
        string[] order = ["Home", "People", "Places", "Media", "Sources", "Charts", "Tools"];
        for (var i = 0; i < order.Length; i++)
        {
            var ws = order[i];
            Bind(() => Navigate(ws), Key.D1 + i, ModifierKeys.Control);
            Bind(() => Navigate(ws), Key.NumPad1 + i, ModifierKeys.Control);
        }
    }

    private void UpdateNavigation()
    {
        BackButton.IsEnabled = _state.CanGoBack;
        ForwardButton.IsEnabled = _state.CanGoForward;
    }

    private void OnBack(object sender, RoutedEventArgs e) => _state.GoBack();

    private void OnForward(object sender, RoutedEventArgs e) => _state.GoForward();

    /// <summary>Person > Go to Home Person (Ctrl+Home).</summary>
    public void GoHome()
    {
        if (_state.Doc.HomePerson is { } home) _state.Select(home, focus: true);
    }

    public void ToggleBookmark()
    {
        if (_state.Selected is { } p) _state.ToggleBookmark(p);
    }

    private TreeDocument InitialDocument(string? openPath)
    {
        if (!_screenshot)
        {
            var path = openPath ?? (_settings.ReopenLastTree ? _settings.RecentFiles.FirstOrDefault(File.Exists) : null);
            if (path is not null)
            {
                try { return LoadAny(path); }
                catch (Exception) { /* fall through to the sample tree */ }
            }
        }
        return new TreeDocument(SampleTree.Create());
    }

    private static TreeDocument LoadAny(string path)
    {
        if (Path.GetExtension(path).Equals(".ged", StringComparison.OrdinalIgnoreCase))
            return new TreeDocument(Gedcom.Import(path));
        return TreeDocument.Load(path);
    }

    private void UpdateChrome()
    {
        var doc = _state.Doc;
        FileTitle.Text = doc.Title + (doc.IsDirty ? "  •  unsaved changes" : "");
        Title = $"{doc.Title} - Mycelia";
        UndoButton.IsEnabled = doc.CanUndo;
        RedoButton.IsEnabled = doc.CanRedo;
        var c = doc.Counts();
        StatusLeft.Text = $"{c.People} people   ·   {c.Families} families   ·   {c.Places} places   ·   {c.Sources} sources   ·   {c.Media} media";
        StatusRight.Text = doc.HomePerson is { } home ? $"Home person: {home.DisplayName}" : "";
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string name }) return;
        _workspace = name;
        foreach (UIElement child in WorkspaceHost.Children)
        {
            if (child is FrameworkElement fe)
                fe.Visibility = fe.Name == name + "View" ? Visibility.Visible : Visibility.Collapsed;
        }
        if (WorkspaceHost.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Name == name + "View") is IWorkspace ws)
            ws.OnShown();
    }

    public void Navigate(string workspace)
    {
        // "Person" is the People workspace's Person view.
        if (workspace == "Person")
        {
            Navigate("People");
            PeopleView.ShowPersonView();
            return;
        }
        var tab = NavTabs.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == workspace);
        if (tab is not null) tab.IsChecked = true;
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
        _state.Doc.Undo();
    }

    private void OnRedo(object sender, RoutedEventArgs e) => _state.Doc.Redo();

    private void OnSave(object sender, RoutedEventArgs e) => Save(saveAs: false);

    private void OnMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var sel = _state.Selected;
        MenuItem Item(string header, string? icon, Action action, string? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (icon is not null) item.Icon = UiKit.Icon(icon, 16, UiKit.Brush("Muted"));
            item.Click += (_, _) => action();
            return item;
        }
        MenuItem Sub(string header, string icon, params object[] items)
        {
            var sub = new MenuItem { Header = header, Icon = UiKit.Icon(icon, 16, UiKit.Brush("Muted")) };
            foreach (var i in items) sub.Items.Add(i);
            return sub;
        }

        menu.Items.Add(Item("New tree", "Plus", NewTree, "Ctrl+N"));
        menu.Items.Add(Item("Open...", "Open", OpenFile, "Ctrl+O"));
        menu.Items.Add(Item("Save", "Save", () => Save(false), "Ctrl+S"));
        menu.Items.Add(Item("Save as...", "Save", () => Save(true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Sub("Import and export", "Export",
            Item("Import GEDCOM...", null, ImportGedcom),
            Item("Merge another tree into this one...", null, MergeTree),
            new Separator(),
            Item("Export GEDCOM...", null, () => ExportGedcom(branch: false)),
            Item("Export a branch...", null, () => ExportGedcom(branch: true)),
            new Separator(),
            Item("Back up with media...", null, BackupTree),
            Item("Restore a backup...", null, RestoreBackup)));
        menu.Items.Add(Sub("Edit", "Edit",
            Item("Find and replace...", null, () => Dialogs.FindAndReplace(_state), "Ctrl+F"),
            new Separator(),
            Item("Manage fact types...", null, () => Dialogs.ManageFactTypes(_state)),
            Item("Relationships...", null, ShowRelationships),
            Item("Historical events...", null, () => Dialogs.HistoricalEvents(_state))));
        menu.Items.Add(Sub("Person", "Person",
            Item("Back", null, _state.GoBack, "Alt+Left", _state.CanGoBack),
            Item("Forward", null, _state.GoForward, "Alt+Right", _state.CanGoForward),
            Item("Go to home person", null, GoHome, "Ctrl+Home", _state.Doc.HomePerson is not null),
            Item("Make home person", null, () => { if (_state.Selected is { } p) PersonActions.SetHome(_state, p); }, null, sel is not null && sel != _state.Doc.HomePerson),
            new Separator(),
            Item(sel is not null && _state.IsBookmarked(sel) ? "Remove bookmark" : "Add bookmark", null, ToggleBookmark, "Ctrl+Shift+B", sel is not null),
            Item("Set spouse order...", null, () => { if (_state.Selected is { } p) Dialogs.SpouseOrder(_state, p); }, null, sel is not null),
            new Separator(),
            Item("Merge two people...", null, () => PersonActions.Merge(_state, _state.Selected, null)),
            Item(sel is not null ? $"Delete {sel.DisplayName}..." : "Delete person...", null, () =>
            {
                if (_state.Selected is { } p) PersonActions.Delete(_state, p);
            }, null, sel is not null)));
        menu.Items.Add(Sub("Tools", "Tools",
            Item("Find duplicate people...", null, FindDuplicates),
            Item("Sort all children by birth...", null, SortChildren),
            Item("Convert names...", null, ConvertNames),
            new Separator(),
            Item("Check for updates...", null, CheckForUpdates),
            Item("Options...", null, OpenOptions)));
        menu.Items.Add(Sub("Help", "Book",
            Item("Help for this screen", null, () => Help.Open(CurrentHelpPage()), "F1"),
            Item("All help", null, () => Help.Open(Help.Home)),
            Item("Keyboard shortcuts", null, () => Help.Open(Help.KeyboardShortcuts)),
            new Separator(),
            Item("Report a problem or suggest an idea...", null, () => Dialogs.OpenWebAddress(Help.IssuesAddress))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open sample tree", "Tree", () => { if (ConfirmDiscard()) _state.SetDocument(new TreeDocument(SampleTree.Create())); }));
        menu.Items.Add(Item("Exit", "Close", Close));
        menu.IsOpen = true;
    }

    /// <summary>The help page for what is in front: the Assistant when it has the keyboard, else the workspace (or the Person view).</summary>
    private string CurrentHelpPage()
    {
        if (_pane is { IsKeyboardFocusWithin: true }) return Help.Assistant;
        if (_workspace == "People" && PeopleView.IsPersonViewShown) return Help.ForWorkspace("Person");
        return Help.ForWorkspace(_workspace);
    }

    /// <summary>Edit > Relationships: go to the chosen couple's first partner in the People workspace.</summary>
    public void ShowRelationships()
    {
        if (Dialogs.Relationships(_state) is not { } f) return;
        var p = _state.Doc.GetPerson(f.Partner1Id) ?? _state.Doc.GetPerson(f.Partner2Id);
        if (p is null) return;
        _state.Select(p, focus: true);
        Navigate("People");
    }

    /// <summary>Tools > Sort all children by birth: one undo step.</summary>
    public void SortChildren()
    {
        if (MessageBox.Show(this, "Put every family's children in birth order?  Children with no birth date go last.  Undo reverses it.",
                "Sort children", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var count = 0;
        _state.Doc.Change(() => count = TreeTools.SortAllChildrenByBirth(_state.Doc));
        MessageBox.Show(this, count == 0 ? "Every family was already in birth order." : count == 1 ? "1 family reordered." : $"{count} families reordered.",
            "Sort children", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Tools > Convert Names: one undo step.</summary>
    public void ConvertNames()
    {
        if (Dialogs.ConvertNames() is not bool caps) return;
        var count = 0;
        _state.Doc.Change(() => count = TreeTools.ConvertNames(_state.Doc, caps));
        MessageBox.Show(this, count == 0 ? "No names needed changing." : count == 1 ? "1 name changed." : $"{count} names changed.",
            "Convert names", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>File > Merge another tree: adds a .ftree or GEDCOM file to the open tree as one undo step.</summary>
    public void MergeTree()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Merge another tree into this one",
            Filter = "Family trees (*.ftree;*.ged)|*.ftree;*.ged|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        TreeData other;
        try { other = TreeImport.Read(_state.Doc, dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The file could not be read.\n\n{ex.Message}", "Merge trees", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (other.People.Count == 0)
        {
            MessageBox.Show(this, "That tree has nobody in it.", "Merge trees", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Add {other.People.Count} people and {other.Families.Count} families from \"{Path.GetFileName(dlg.FileName)}\" to this tree?  Places and sources with the same names are shared.  Undo reverses it.",
                "Merge trees", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        List<string> added = [];
        _state.Doc.Change(() => added = TreeImport.MergeInto(_state.Doc, other));
        if (_state.Doc.GetPerson(added.FirstOrDefault()) is { } first) _state.Select(first, focus: true);
        if (MessageBox.Show(this, $"{added.Count} people added.  People who were in both trees now appear twice.  Look for duplicates now?",
                "Merge trees", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            FindDuplicates();
        else
            Navigate("People");
    }

    /// <summary>File > Backup: the tree and copies of its media in one file.</summary>
    public void BackupTree()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Back up the tree with its media",
            Filter = $"Mycelia backups (*{TreeImport.BackupExtension})|*{TreeImport.BackupExtension}",
            DefaultExt = TreeImport.BackupExtension,
            FileName = $"{_state.Doc.Data.Name} {DateTime.Now:yyyy-MM-dd}",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var missing = TreeImport.Backup(_state.Doc, dlg.FileName);
            var text = $"Backed up {_state.Doc.Data.People.Count} people and {_state.Doc.Data.Media.Count - missing.Count} media files.";
            if (missing.Count > 0)
                text += $"\n\n{missing.Count} media files could not be found and were left out:\n" + string.Join("\n", missing.Take(8)) + (missing.Count > 8 ? "\n..." : "");
            MessageBox.Show(this, text, "Backup", MessageBoxButton.OK, missing.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The backup could not be written.\n\n{ex.Message}", "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>File > Restore: unpacks a backup into a folder and opens the tree.</summary>
    public void RestoreBackup()
    {
        if (!ConfirmDiscard()) return;
        var open = new OpenFileDialog
        {
            Title = "Restore a backup",
            Filter = $"Mycelia backups (*{TreeImport.BackupExtension})|*{TreeImport.BackupExtension}",
        };
        if (open.ShowDialog(this) != true) return;
        var folder = new OpenFolderDialog { Title = "Choose the folder to restore the tree and its media into" };
        if (folder.ShowDialog(this) != true) return;
        try
        {
            var path = TreeImport.Restore(open.FileName, folder.FolderName);
            OpenPath(path);
            MessageBox.Show(this, $"Restored to {path}.  Media files are in the Media folder beside it.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The backup could not be restored.\n\n{ex.Message}", "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Tools > Options; display changes take effect at once, and the MCP servers reconnect where their settings changed.</summary>
    public void OpenOptions() => OpenOptions(null);

    public void OpenOptions(string? page)
    {
        var dialog = new OptionsDialog(_settings, _hub, _tools.All, page);
        dialog.CheckForUpdatesNow = channel => Updates.UpdateService.CheckManuallyAsync(dialog, this, channel);
        dialog.ConnectedPrograms = () => _server?.Clients ?? [];
        var before = (_settings.McpServer.Enabled, _settings.McpServer.Port, _settings.McpServer.Token);
        if (dialog.ShowDialog() != true) return;
        if ((_settings.McpServer.Enabled, _settings.McpServer.Port, _settings.McpServer.Token) != before) StartServer();
        foreach (var p in _state.Doc.Data.People) p.RaiseAll();
        _state.Refresh();
        _assistant.ApplyBuiltInPolicies();
        _pane?.Refresh();
        if (!_screenshot) _ = _hub.ApplyAsync(_settings.McpServers, CancellationToken.None);
    }

    /// <summary>The Options window at a tab, for a check picture (--options with --screenshot); nothing can be saved in that mode.</summary>
    public Window OptionsForPicture(string page)
    {
        // --options "MCP Helper": the MCP Helper as it opens, with the genealogy list.
        if (page == "MCP Helper")
            return new McpHelperWindow(_settings.Assistant);
        // MCP servers: an example server that signs in through the browser, so the picture shows those settings.
        if (page == "MCP servers" && _settings.McpServers.Count == 0)
            _settings.McpServers.Add(new McpServerConfig
            {
                Id = "example", Name = "Example archive", Enabled = false, Transport = McpTransportKind.Http,
                Url = "https://mcp.example.org/mcp", OAuth = new McpOAuthSettings { Enabled = true },
            });
        var dialog = new OptionsDialog(_settings, _hub, _tools.All, page) { ConnectedPrograms = () => _server?.Clients ?? [] };
        dialog.Owner = null;
        return dialog;
    }

    // ---- Other programs using the tools (TDD 24) ----

    private Ai.Server.McpServer? _server;

    /// <summary>Starts, restarts or stops the server as Options › Other programs says; never in screenshot mode.</summary>
    private void StartServer()
    {
        if (_server is not null)
        {
            var old = _server;
            _server = null;
            Task.Run(() => old.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
        }
        var s = _settings.McpServer;
        if (_screenshot || !s.Enabled)
        {
            UpdateServingStatus();
            return;
        }
        var token = Ai.SecretStore.Unprotect(s.Token);
        if (token.Length == 0)
        {
            token = Ai.Server.McpServerSettings.NewToken();
            s.Token = Ai.SecretStore.Protect(token);
            _settings.Save();
        }
        try
        {
            var server = new Ai.Server.McpServer(this, s.Port, token);
            server.ClientsChanged += () => Dispatcher.BeginInvoke(UpdateServingStatus);
            server.Start();
            _server = server;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _hub.Log($"[server] Couldn't listen on port {s.Port}: {ex.Message}");
            StatusRight.Text = $"Other programs can't connect: port {s.Port} is in use.  Choose another in Options › Other programs.";
        }
        UpdateServingStatus();
    }

    private void UpdateServingStatus()
    {
        var clients = _server?.Clients ?? [];
        ServingStatus.Text = _server is null ? ""
            : clients.Count == 0 ? "Other programs: on"
            : $"Connected: {string.Join(", ", clients)}";
        ServingStatus.ToolTip = _server is null ? null : $"Mycelia's tools are served at http://127.0.0.1:{_server.Port}/mcp (Options › Other programs).";
    }

    /// <summary>An approval for another program needs the Assistant on show and open.</summary>
    private void ShowAssistantForApproval()
    {
        if (!AssistantOpen) ShowAssistant();
        if (_assistantPanel is { IsFolded: true } panel) panel.SetFolded(false, byPerson: true);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>A server the Assistant mustn't connect to: Mycelia's own relay, or its own server's address.</summary>
    private string? RefuseSelf(McpServerConfig config)
    {
        const string why = "This is Mycelia itself (Options › Other programs); the Assistant already has these tools.";
        if (config.Transport == McpTransportKind.Stdio)
        {
            var command = config.Command.Trim('"');
            var self = Environment.ProcessPath is { } p && (string.Equals(Path.GetFullPath(command), p, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(command).Equals("Mycelia", StringComparison.OrdinalIgnoreCase));
            return self && config.Arguments.Contains("--mcp-relay") ? why : null;
        }
        return Uri.TryCreate(config.Url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Port == _settings.McpServer.Port ? why : null;
    }

    // IMcpServerHost: what the server needs; it calls from its own threads.
    IReadOnlyList<AssistantTool> Ai.Server.IMcpServerHost.Tools() => _tools.All.Where(t => t.Policy != ToolPolicy.Off).ToList();
    string Ai.Server.IMcpServerHost.Instructions() => Dispatcher.Invoke(_assistant.InstructionsForPrograms);
    Task<ToolOutcome> Ai.Server.IMcpServerHost.RunAsync(AssistantTool tool, JsonObject arguments, string client, CancellationToken cancel) =>
        Dispatcher.InvokeAsync(() => _assistant.RunForProgramAsync(tool, arguments, client, cancel)).Task.Unwrap();
    void Ai.Server.IMcpServerHost.Log(string line) => _hub.Log(line);

    // ---- Software updates (TDD 23) ----

    /// <summary>Tools > Check for updates...: always checks and reports what it found.</summary>
    public void CheckForUpdates() => _ = Updates.UpdateService.CheckManuallyAsync(this, this);

    private void OnUpdateNotice(object sender, RoutedEventArgs e) => CheckForUpdates();

    /// <summary>Set once the person has answered the save question for an update, so closing doesn't ask again.</summary>
    private bool _closingForUpdate;

    AppSettings Updates.IUpdateHost.Settings => _settings;

    void Updates.IUpdateHost.ShowUpdateNotice(string? version, bool required)
    {
        UpdateButton.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateLabel.Text = required ? "Update required" : "Update available";
        UpdateButton.ToolTip = version is null ? null
            : $"{Updates.UpdateConfig.DisplayName} {version} is {(required ? "required" : "available")}.  Click to download and install it.";
    }

    void Updates.IUpdateHost.ShowStatus(string text) => StatusRight.Text = text;

    bool Updates.IUpdateHost.CloseForUpdate()
    {
        if (!ConfirmDiscard()) return false;
        _closingForUpdate = true;
        Close();
        // Closing can still be stopped; then nothing is installed.
        if (IsVisible) _closingForUpdate = false;
        return !IsVisible;
    }

    // ---- The Assistant pane (TDD 22.1) ----

    /// <summary>
    /// Narrowest the pane may be; the room kept for a workspace without foldable panels (Home, Tools, the Person view);
    /// and the workspace's own margin on each side.
    /// </summary>
    private const double AssistantMinWidth = 300, WorkspaceMinWidth = 520, WorkspaceMargin = 12;

    private FoldPanel? _assistantPanel;

    private bool AssistantOpen => AssistantHost.Content is not null;

    private void OnAssistant(object sender, RoutedEventArgs e) => ToggleAssistant();

    public void ToggleAssistant()
    {
        if (AssistantOpen) HideAssistant();
        else ShowAssistant();
    }

    /// <summary>The selected person above the Assistant's message box: "Josiah Ashworth, 1798-1871", or the whole tree.</summary>
    private void ShowAssistantSubject()
    {
        if (_pane is null) return;
        var person = _state.Selected;
        _pane.ShowAbout(person is null ? null : person.LifeSpan.Length > 0 ? $"{person.DisplayName}, {person.LifeSpan}" : person.DisplayName);
    }

    /// <summary>Opens the Assistant as a panel on the right, folded or open as it was left (TDD 22.1).</summary>
    public void ShowAssistant(bool demo = false)
    {
        if (_pane is null)
        {
            _pane = new AssistantPane(_assistant, _settings.Assistant, () => OpenOptions("AI model"), HideAssistant);
            _assistantPanel = new FoldPanel
            {
                Key = "Assistant",
                Title = "Assistant",
                Icon = "Assistant",
                Side = FoldSide.Right,
                MinSize = AssistantMinWidth,
                HeaderTools = _pane.Tools,
                Body = _pane,
            };
            _assistantPanel.EnsureBuilt();
            // Whom a request is about, kept up as the selection, the tree or a name changes (TDD 22.1).
            ShowAssistantSubject();
            _state.SelectionChanged += ShowAssistantSubject;
            _state.DocumentReplaced += ShowAssistantSubject;
            _state.TreeChanged += ShowAssistantSubject;
            _assistantPanel.SetFolded(_settings.Assistant.PaneFolded);
            _assistantPanel.FoldedChanged += UpdateAssistantColumn;
            _assistantPanel.FoldToggled += () =>
            {
                _settings.Assistant.PaneFolded = _assistantPanel.IsFolded;
                _settings.Save();
                if (!_assistantPanel.IsFolded) _pane.FocusInput();
            };
        }
        AssistantHost.Content = _assistantPanel;
        UpdateAssistantColumn();
        if (demo && _assistant.Items.Count == 0) _assistant.AddDemo();
        _pane.Refresh();
        if (!_assistantPanel!.IsFolded) _pane.FocusInput();
        if (!_settings.Assistant.PaneVisible)
        {
            _settings.Assistant.PaneVisible = true;
            _settings.Save();
        }
    }

    public void HideAssistant()
    {
        AssistantColumn.Width = new GridLength(0);
        AssistantSplitter.Visibility = Visibility.Collapsed;
        AssistantHost.Content = null;
        _settings.Assistant.PaneVisible = false;
        _settings.Save();
    }

    /// <summary>The pane's column: a strip when folded, else its width within the room the workspace leaves.</summary>
    private void UpdateAssistantColumn()
    {
        if (!AssistantOpen || _assistantPanel is null) return;
        var folded = _assistantPanel.IsFolded;
        var width = folded ? FoldPanel.StripWidth : ClampAssistantWidth(_settings.Assistant.PaneWidth);
        AssistantColumn.Width = new GridLength(width + WorkspaceMargin);
        AssistantSplitter.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Window widths below which the top bar drops words: first on Assistant and Save, then on the tabs.</summary>
    private const double CompactButtonsWidth = 1240, CompactTabsWidth = 1140;

    /// <summary>
    /// Keeps the top bar from overlapping in a narrow window: the Assistant and Save buttons, then the workspace
    /// tabs, show their icons only (each tab keeps its name as a tooltip).
    /// </summary>
    private void FitTopBar()
    {
        var buttons = ActualWidth < CompactButtonsWidth ? Visibility.Collapsed : Visibility.Visible;
        AssistantLabel.Visibility = buttons;
        SaveLabel.Visibility = buttons;
        UpdateLabel.Visibility = buttons;
        var tabs = ActualWidth < CompactTabsWidth ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tab in NavTabs.Children.OfType<RadioButton>())
            if (tab.Content is StackPanel panel)
                foreach (var label in panel.Children.OfType<TextBlock>()) label.Visibility = tabs;
    }

    /// <summary>Folds panels by their settings key ("People.Index"), or the Assistant ("Assistant"), for --fold.</summary>
    public void FoldPanels(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (key.Equals("Assistant", StringComparison.OrdinalIgnoreCase)) _assistantPanel?.SetFolded(true);
            else FoldLayout.FoldByKey(key);
        }
    }

    /// <summary>
    /// The pane's top follows the top of the workspace's panels (in People they sit under the Tree and Person switch),
    /// so its header lines up with theirs; with no foldable panels on show it follows the workspace margin.  Margins
    /// are set only when they change, so this settles after one layout pass.
    /// </summary>
    private void AlignAssistant()
    {
        if (!AssistantOpen) return;
        var top = WorkspaceMargin;
        if (FoldLayout.Visible() is { } layout && layout.IsVisible && layout.ActualHeight > 0)
            top = Math.Max(top, Math.Round(layout.TranslatePoint(new Point(0, 0), Body).Y));
        var host = new Thickness(0, top, WorkspaceMargin, WorkspaceMargin);
        if (AssistantHost.Margin != host) AssistantHost.Margin = host;
        var handle = new Thickness(-WorkspaceMargin, top, 0, WorkspaceMargin);
        if (AssistantSplitter.Margin != handle) AssistantSplitter.Margin = handle;
    }

    /// <summary>
    /// The pane's width kept between its least and what leaves the workspace's open panels their least.  When even its
    /// least doesn't leave that, the workspace folds panels to make room (FoldLayout.ArrangeOverride).
    /// </summary>
    private double ClampAssistantWidth(double width)
    {
        var workspace = FoldLayout.Visible() is { } layout ? layout.MinimumWidth + 2 * WorkspaceMargin : WorkspaceMinWidth;
        var room = (Body.ActualWidth > 0 ? Body.ActualWidth : Width) - workspace - WorkspaceMargin;
        return Math.Max(AssistantMinWidth, Math.Min(width, room));
    }

    private void OnAssistantDrag(object sender, DragDeltaEventArgs e)
    {
        var width = ClampAssistantWidth(AssistantColumn.ActualWidth - WorkspaceMargin - e.HorizontalChange);
        AssistantColumn.Width = new GridLength(width + WorkspaceMargin);
    }

    private void OnAssistantDragDone(object sender, DragCompletedEventArgs e)
    {
        _settings.Assistant.PaneWidth = AssistantColumn.ActualWidth - WorkspaceMargin;
        _settings.Save();
    }

    // IAssistantHost: what the Assistant needs from the window.
    AssistantSettings IAssistantHost.Settings => _settings.Assistant;
    /// <summary>
    /// A server's browser sign-in got new tokens (signed in, refreshed, or refused and forgotten) in a working copy (the
    /// hub's or the Options window's): they go into the saved server with the same id and address, and are saved.
    /// </summary>
    private void McpTokensChanged(McpServerConfig changed)
    {
        void Keep()
        {
            var saved = _settings.McpServers.FirstOrDefault(s => string.Equals(s.Id, changed.Id, StringComparison.OrdinalIgnoreCase));
            if (saved == null || !string.Equals(saved.Url.Trim(), changed.Url.Trim(), StringComparison.OrdinalIgnoreCase))
                return;
            if (!ReferenceEquals(saved, changed))
                saved.OAuth.TakeTokensFrom(changed.OAuth);
            _settings.Save();
        }
        if (Dispatcher.CheckAccess()) Keep();
        else Dispatcher.BeginInvoke(Keep);
    }

    IReadOnlyList<McpServerConfig> IAssistantHost.Servers => _settings.McpServers;
    void IAssistantHost.SaveSettings() => _settings.Save();
    bool IAssistantHost.KeepConversations => !_screenshot;
    IReadOnlyList<AssistantTool> IAssistantHost.BuiltInTools => _tools.All;
    JsonObject IAssistantHost.Context() => _tools.Context();
    string IAssistantHost.ContextLine() => _tools.ContextLine();
    void IAssistantHost.BeginRequest() => _tools.BeginRequest();
    (int Changes, Func<string>? Revert) IAssistantHost.EndRequest() => _tools.EndRequest();

    // IToolHost: what the tree tools need from the window (TDD 22.3).
    AssistantSettings IToolHost.Settings => _settings.Assistant;
    string IToolHost.Workspace => _workspace == "People" && PeopleView.IsPersonViewShown ? "Person" : _workspace;
    void IToolHost.Navigate(string workspace) => Navigate(workspace);

    void IToolHost.ShowChart(Ui.Charts.ChartKind kind, int? generations)
    {
        Navigate("Charts");
        ChartsView.Show(kind, generations);
    }

    string? IToolHost.SaveTree(string? path)
    {
        try
        {
            var target = path ?? _state.Doc.FilePath ?? throw new InvalidOperationException("The tree has no file yet.");
            _state.Doc.Save(target);
            _settings.AddRecent(target);
            UpdateChrome();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The tree could not be saved: {ex.Message}";
        }
    }

    public void FindDuplicates()
    {
        Navigate("Tools");
        ToolsView.ShowDuplicates();
    }

    public bool ConfirmDiscard()
    {
        if (!_state.Doc.IsDirty) return true;
        var answer = MessageBox.Show(this, $"Save changes to \"{_state.Doc.Title}\"?", "Mycelia", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) return Save(false);
        return true;
    }

    public void NewTree()
    {
        if (!ConfirmDiscard()) return;
        _state.SetDocument(new TreeDocument(new TreeData { Name = "New family tree" }));
        Navigate("People");
    }

    public void OpenFile()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Title = "Open a family tree",
            Filter = "Family trees (*.ftree;*.ged)|*.ftree;*.ged|Mycelia trees (*.ftree)|*.ftree|GEDCOM files (*.ged)|*.ged|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        OpenPath(dlg.FileName);
    }

    public void OpenPath(string path)
    {
        try
        {
            var doc = LoadAny(path);
            _state.SetDocument(doc);
            if (doc.FilePath is not null) _settings.AddRecent(doc.FilePath);
            Navigate("People");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The file could not be opened.\n\n{ex.Message}", "Mycelia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public bool Save(bool saveAs)
    {
        var doc = _state.Doc;
        var path = doc.FilePath;
        if (saveAs || path is null)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save family tree",
                Filter = "Mycelia trees (*.ftree)|*.ftree",
                DefaultExt = TreeDocument.FileExtension,
                FileName = doc.Data.Name,
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            doc.Save(path);
            _settings.AddRecent(path);
            UpdateChrome();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The tree could not be saved.\n\n{ex.Message}", "Mycelia", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void ImportGedcom()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Title = "Import a GEDCOM file", Filter = "GEDCOM files (*.ged)|*.ged|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var doc = new TreeDocument(Gedcom.Import(dlg.FileName));
            _state.SetDocument(doc);
            Navigate("People");
            MessageBox.Show(this, $"Imported {doc.Data.People.Count} people and {doc.Data.Families.Count} families.  Save the tree to keep it.",
                "Import GEDCOM", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The GEDCOM file could not be read.\n\n{ex.Message}", "Mycelia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>File > Export: the whole tree or a branch, optionally privatising living people.</summary>
    public void ExportGedcom(bool branch)
    {
        if (Dialogs.ExportOptions(_state.Doc, _state.Selected, branch) is not { } options) return;
        var dlg = new SaveFileDialog
        {
            Title = branch ? "Export branch" : "Export GEDCOM",
            Filter = "GEDCOM 5.5.1 (*.ged)|*.ged",
            DefaultExt = ".ged",
            FileName = _state.Doc.Data.Name,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Gedcom.Export(_state.Doc, dlg.FileName, options);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The GEDCOM file could not be written.\n\n{ex.Message}", "Mycelia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // An update has already asked to save the tree (CloseForUpdate), so don't ask twice.
        if (!_screenshot && !_closingForUpdate && !ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
        if (e.Cancel) return;
        // Stop a running request, keep the conversation, and close the MCP servers' programs (two seconds each at most).
        _assistant.Shutdown();
        if (_server is { } server) Task.Run(() => server.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
        try { Task.Run(() => _hub.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { /* a server that won't close is stopped with the process */ }
    }
}

/// <summary>A workspace that wants to know when it comes into view.</summary>
public interface IWorkspace
{
    void OnShown();
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
