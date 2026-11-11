using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mycelia.Ai.Assistant;

namespace Mycelia.Ui;

/// <summary>
/// The Assistant pane on the right of every workspace (TDD 22.1): the conversation, approval cards for tools set to
/// Ask, and the box to type a request.  Built in code like the other screens; each chat item is drawn again when it
/// changes (a tool finishing, an approval answered).  It is the body of a <see cref="FoldPanel"/>, whose header
/// carries the title and <see cref="Tools"/>.
/// </summary>
public sealed class AssistantPane : Border
{
    private readonly AssistantSession _session;
    private readonly AssistantSettings _settings;
    private readonly Action _openOptions;
    private readonly Action _close;
    private readonly StackPanel _items = new() { Margin = new Thickness(16, 10, 16, 16) };
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _modelLine = UiKit.Text("", "Caption");
    private readonly TextBlock _toolsLine = UiKit.Text("", "Caption");
    private readonly Border _keyBanner;
    private readonly TextBlock _busy = UiKit.Text("", "Caption");
    private readonly TextBlock _about = UiKit.Text("About: the whole tree", "Caption");
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly Dictionary<ChatItem, ContentControl> _hosts = [];
    private readonly HashSet<ChatItem> _expanded = [];

    public AssistantPane(AssistantSession session, AssistantSettings settings, Action openOptions, Action close)
    {
        _session = session;
        _settings = settings;
        _openOptions = openOptions;
        _close = close;
        var root = new DockPanel();

        // The panel header's buttons; the header itself belongs to the FoldPanel.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UiKit.IconButton("Copy", "Copy the conversation", (_, _) => CopyConversation()));
        buttons.Children.Add(UiKit.IconButton("Trash", "Clear the conversation", (_, _) => ClearConversation()));
        buttons.Children.Add(UiKit.IconButton("Close", "Close the Assistant (the Assistant button brings it back)", (_, _) => _close()));
        Tools = buttons;

        // Under the header: the model and the tools on offer.
        var info = new StackPanel { Margin = new Thickness(16, 8, 16, 8) };
        info.Children.Add(_modelLine);
        info.Children.Add(_toolsLine);
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        // No key yet: say where to add one.
        var bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Brush("Ink"), VerticalAlignment = VerticalAlignment.Center };
        bannerText.Text = "Choose an AI model and add its API key before asking.";
        var bannerButton = UiKit.Button("GhostButton", "Options", (_, _) => _openOptions());
        DockPanel.SetDock(bannerButton, Dock.Right);
        _keyBanner = new Border
        {
            Background = UiKit.Brush("WarningSoft"),
            BorderBrush = UiKit.Brush("Warning"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(16, 0, 16, 8),
            Child = new DockPanel { Children = { bannerButton, bannerText } },
        };
        DockPanel.SetDock(_keyBanner, Dock.Top);
        root.Children.Add(_keyBanner);
        var divider = new Border { Height = 1, Background = UiKit.Brush("Border") };
        DockPanel.SetDock(divider, Dock.Top);
        root.Children.Add(divider);

        // The box to type in, with Send (Stop while a request runs).
        var footer = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        _busy.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_busy, Dock.Top);
        footer.Children.Add(_busy);
        // Whom a request is about: the selected person, or the whole tree (TDD 22.1).
        var about = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var aboutIcon = UiKit.Icon("Person", 14, UiKit.Brush("Muted"));
        aboutIcon.Margin = new Thickness(0, 0, 6, 0);
        about.Children.Add(aboutIcon);
        about.Children.Add(_about);
        DockPanel.SetDock(about, Dock.Top);
        footer.Children.Add(about);
        _send = new Button { Style = UiKit.Res<Style>("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Height = 36, MinWidth = 44 };
        _send.Click += (_, _) => SendOrStop();
        DockPanel.SetDock(_send, Dock.Right);
        footer.Children.Add(_send);
        _input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 56,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = "Enter sends; Shift+Enter starts a new line.  /clear starts a new conversation.",
        };
        // Enter sends; Shift+Enter starts a new line.
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                if (!_session.IsBusy) SendOrStop();
            }
        };
        footer.Children.Add(_input);
        var footerBorder = new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(footerBorder);

        _scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _items };
        root.Children.Add(_scroller);
        Child = root;

        _session.Items.CollectionChanged += OnItemsChanged;
        _session.BusyChanged += Refresh;
        _session.Hub.Changed += () => Dispatcher.BeginInvoke(Refresh);
        RebuildItems();
        Refresh();
    }

    /// <summary>Copy, Clear and Close, for the panel header.</summary>
    public FrameworkElement Tools { get; }

    /// <summary>The selected person as "Josiah Ashworth, 1798-1871", or null for nobody; shown above the message box.</summary>
    public void ShowAbout(string? person)
    {
        _about.Text = person is null ? "About: the whole tree" : "About: " + person;
        _about.ToolTip = person is null
            ? "Nobody is selected, so a request is about the tree as a whole."
            : "The selected person.  \"This person\" in a request means them.";
    }

    public void FocusInput() => Dispatcher.BeginInvoke(() => _input.Focus(), DispatcherPriority.Input);

    /// <summary>Puts a request in the box without sending it.</summary>
    public void Prefill(string text)
    {
        _input.Text = text;
        _input.CaretIndex = text.Length;
        FocusInput();
    }

    /// <summary>The header lines, the key banner and the Send or Stop button.</summary>
    public void Refresh()
    {
        _modelLine.Text = _settings.ModelLine;
        _toolsLine.Text = _session.ToolsSummary;
        _keyBanner.Visibility = _settings.HasKey ? Visibility.Collapsed : Visibility.Visible;
        _busy.Text = _session.BusyText;
        _busy.Visibility = _session.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        _send.Content = _session.IsBusy ? UiKit.IconText("Stop", "Stop", 14) : UiKit.Icon("Send", 16);
        _send.ToolTip = _session.IsBusy ? "Stop the request" : "Send (Enter)";
    }

    private void SendOrStop()
    {
        if (_session.IsBusy)
        {
            _session.Stop();
            return;
        }
        var text = _input.Text;
        if (text.Trim().Length == 0) return;
        _input.Clear();
        _ = _session.SendAsync(text);
    }

    private void ClearConversation()
    {
        if (_session.IsBusy || _session.Items.Count == 0) return;
        if (MessageBox.Show(Window.GetWindow(this), "Clear the conversation?  The Assistant forgets it, and it can't be brought back.",
                "Assistant", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _session.ClearConversation();
    }

    private void CopyConversation()
    {
        if (_session.Items.Count == 0) return;
        try { Clipboard.SetText(_session.ConversationText()); }
        catch (System.Runtime.InteropServices.COMException) { /* the clipboard is busy; the person can try again */ }
    }

    // ---- The conversation ----------------------------------------------------------------------

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null && _hosts.Count > 0)
        {
            foreach (ChatItem item in e.NewItems) AddHost(item);
            ScrollToEnd();
            return;
        }
        RebuildItems();
    }

    private void RebuildItems()
    {
        foreach (var (item, handler) in _handlers) item.Changed -= handler;
        _handlers.Clear();
        _hosts.Clear();
        _expanded.Clear();
        _items.Children.Clear();
        if (_session.Items.Count == 0)
        {
            var empty = UiKit.EmptyState("Assistant", "Ask about your tree",
                "The Assistant reads your tree and, with MCP servers set up in Options, searches record archives and catalogues.  Changes wait for your approval.");
            empty.Margin = new Thickness(0, 60, 0, 0);
            _items.Children.Add(empty);
            return;
        }
        foreach (var item in _session.Items) AddHost(item);
        ScrollToEnd();
    }

    private void AddHost(ChatItem item)
    {
        if (_hosts.Count == 0) _items.Children.Clear();
        var host = new ContentControl { Content = Render(item), Focusable = false };
        _hosts[item] = host;
        // Items change on the UI thread (the session runs there); redraw once the change has finished.
        Action handler = () => Dispatcher.BeginInvoke(() => { if (_hosts.ContainsKey(item)) host.Content = Render(item); });
        _handlers[item] = handler;
        item.Changed += handler;
        _items.Children.Add(host);
    }

    private readonly Dictionary<ChatItem, Action> _handlers = [];

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() => _scroller.ScrollToEnd(), DispatcherPriority.Background);

    private FrameworkElement Render(ChatItem item) => item switch
    {
        UserMessage user => RenderUser(user),
        AssistantMessage assistant => RenderAssistant(assistant),
        NoticeMessage notice => RenderNotice(notice),
        ToolActivity tool => RenderTool(tool),
        ApprovalRequest approval => RenderApproval(approval),
        ChangesMade changes => RenderChanges(changes),
        _ => new Border(),
    };

    /// <summary>"4 changes to the tree · Revert" at the end of a request that changed it (TDD 22.3).</summary>
    private static FrameworkElement RenderChanges(ChangesMade changes)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        var icon = UiKit.Icon("Edit", 14, UiKit.Brush("Brand"));
        icon.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(icon);
        if (changes.CanRevert)
        {
            var revert = new Button
            {
                Style = UiKit.Res<Style>("GhostButton"),
                Content = UiKit.IconText("Undo", "Revert", 13),
                FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                ToolTip = "Put the tree back as it was before this request (Undo brings the changes back)",
            };
            revert.Click += (_, _) => changes.Revert();
            DockPanel.SetDock(revert, Dock.Right);
            row.Children.Add(revert);
        }
        var text = changes.Count == 1 ? "1 change to the tree" : $"{changes.Count} changes to the tree";
        if (changes.Outcome is { } outcome) text += ".  " + outcome;
        else if (changes.CanRevert) text += ".  Each is one Undo step.";
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = UiKit.Brush("Muted"), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Margin = new Thickness(0, 4, 0, 4),
            Child = row,
        };
    }

    private static FrameworkElement RenderUser(UserMessage user)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(40, 6, 0, 6) };
        var bubble = new Border
        {
            Background = UiKit.Brush("BrandSoft"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 7, 11, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock { Text = user.Text, TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Brush("Ink") },
        };
        panel.Children.Add(bubble);
        if (user.Context.Length > 0)
        {
            var context = UiKit.Text(user.Context, "Caption", 11);
            context.HorizontalAlignment = HorizontalAlignment.Right;
            context.Margin = new Thickness(0, 3, 2, 0);
            panel.Children.Add(context);
        }
        return panel;
    }

    private static FrameworkElement RenderAssistant(AssistantMessage message)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(new ChatText(message.Text));
        var copy = new Button { Style = UiKit.Res<Style>("GhostButton"), Content = "Copy", FontSize = 12, Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 2, 0, 0) };
        copy.Click += (_, _) =>
        {
            try { ChatText.CopyFormatted(message.Text); }
            catch (System.Runtime.InteropServices.COMException) { return; }
            copy.Content = "Copied";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); copy.Content = "Copy"; };
            timer.Start();
        };
        panel.Children.Add(copy);
        return panel;
    }

    private static FrameworkElement RenderNotice(NoticeMessage notice)
    {
        var brush = UiKit.Brush(notice.IsError ? "Danger" : "Muted");
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var icon = UiKit.Icon(notice.IsError ? "Warning" : "Note", 14, brush);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 7, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, Foreground = brush, FontSize = 12 });
        return row;
    }

    private FrameworkElement RenderTool(ToolActivity tool)
    {
        var (icon, brush) = tool.State switch
        {
            ToolActivityState.Done => ("Check", "Brand"),
            ToolActivityState.Failed => ("Warning", "Danger"),
            ToolActivityState.Declined => ("Close", "Muted"),
            _ => ("Search", "Muted"),
        };
        var panel = new StackPanel();
        var row = new DockPanel();
        var glyph = UiKit.Icon(icon, 14, UiKit.Brush(brush));
        glyph.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(glyph);
        var arrow = UiKit.Icon("ArrowRight", 12, UiKit.Brush("Subtle"));
        arrow.RenderTransformOrigin = new Point(0.5, 0.5);
        arrow.RenderTransform = new RotateTransform(_expanded.Contains(tool) ? 90 : 0);
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        text.Inlines.Add(new System.Windows.Documents.Run(tool.Title) { FontWeight = FontWeights.SemiBold, Foreground = UiKit.Brush("Ink") });
        text.Inlines.Add(new System.Windows.Documents.Run($"  ·  {tool.Source}  ·  {tool.Summary}") { Foreground = UiKit.Brush("Muted") });
        text.ToolTip = tool.Summary;
        row.Children.Add(text);
        panel.Children.Add(row);
        if (_expanded.Contains(tool))
        {
            panel.Children.Add(Details("Arguments", tool.Arguments));
            if (tool.Result.Length > 0) panel.Children.Add(Details("Result", tool.Result));
        }
        var border = new Border
        {
            Background = UiKit.Brush("SurfaceAlt"),
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 3, 0, 3),
            Cursor = Cursors.Hand,
            Child = panel,
            ToolTip = "Show or hide what was sent and what came back",
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<TextBox>(d) is not null) return;
            if (!_expanded.Remove(tool)) _expanded.Add(tool);
            if (_hosts.TryGetValue(tool, out var host)) host.Content = Render(tool);
        };
        return border;
    }

    private FrameworkElement RenderApproval(ApprovalRequest approval)
    {
        var panel = new StackPanel();
        var heading = approval.FromProgram is { } program
            ? (approval.IsPending ? $"{program} asks: allow this?" : $"{program} asked")
            : (approval.IsPending ? "Allow this?" : "Asked");
        panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Brush("Ink"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = approval.Description, TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Brush("Ink"), Margin = new Thickness(0, 3, 0, 0) });
        panel.Children.Add(new TextBlock { Text = $"{approval.Title}  ·  {approval.Source}", Style = UiKit.Res<Style>("Caption"), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) });
        var toggle = new Button
        {
            Style = UiKit.Res<Style>("GhostButton"),
            Content = _expanded.Contains(approval) ? "Hide details" : "Show details",
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-6, 4, 0, 0),
        };
        toggle.Click += (_, _) =>
        {
            if (!_expanded.Remove(approval)) _expanded.Add(approval);
            if (_hosts.TryGetValue(approval, out var host)) host.Content = Render(approval);
        };
        panel.Children.Add(toggle);
        if (_expanded.Contains(approval)) panel.Children.Add(Details("Arguments", approval.Arguments));
        if (approval.IsPending)
        {
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button Answer(string style, string text, bool allowed, bool always)
            {
                var b = UiKit.Button(style, text, (_, _) => approval.Answer(allowed, always));
                b.Margin = new Thickness(0, 0, 6, 0);
                b.Padding = new Thickness(12, 4, 12, 4);
                return b;
            }
            buttons.Children.Add(Answer("PrimaryButton", "Allow", true, false));
            buttons.Children.Add(Answer("SecondaryButton", approval.AlwaysLabel, true, true));
            buttons.Children.Add(Answer("SecondaryButton", "Decline", false, false));
            panel.Children.Add(buttons);
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = approval.Outcome, Style = UiKit.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0) });
        }
        return new Border
        {
            Background = UiKit.Brush(approval.IsPending ? "WarningSoft" : "SurfaceAlt"),
            BorderBrush = UiKit.Brush(approval.IsPending ? "Warning" : "Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 8, 11, 9),
            Margin = new Thickness(0, 4, 0, 4),
            Child = panel,
        };
    }

    private static FrameworkElement Details(string label, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Style = UiKit.Res<Style>("SectionLabel"), Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        return panel;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var current = d; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
