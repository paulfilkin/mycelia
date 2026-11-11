using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mycelia.Ai.Assistant;
using Mycelia.Ai.Mcp;

namespace Mycelia.Ui;

/// <summary>
/// The MCP Helper, over Options › MCP servers (TDD 22.9): the person picks an archive from the genealogy list, or names a
/// server (or gives its address, its documentation page or a configuration snippet); the helper looks it up in the
/// official MCP Registry, asks the address how it signs in (McpProbe), reads its documentation, has the AI model explain
/// what to do in that product (when one is set up), asks for the details still needed, and fills in the server's
/// settings (SetupPlan.FillIn).  Ported from multifariousStudio's Windows/McpHelperWindow.cs with this app's theme and
/// the genealogy list added.
/// </summary>
public sealed class McpHelperWindow : Window
{
    private readonly AssistantSettings _settings;
    private readonly TextBox _query;
    private readonly Button _lookUp;
    private readonly StackPanel _matchesPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    private readonly ListBox _matches;
    private readonly ChatText _explain = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly DockPanel _explainHeader = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Button _copy;
    private readonly StackPanel _questionsPanel = new() { Visibility = Visibility.Collapsed };
    /// <summary>The questions asked about the steps and the helper's answers, as a conversation under the steps.</summary>
    private readonly StackPanel _conversation = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly List<(string Question, string Answer)> _asked = new();
    private readonly StackPanel _askPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel _suggestions = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly Button _ask;
    private readonly ScrollViewer _scroll;
    private readonly TextBox _followUp;
    private readonly TextBlock _status;
    private readonly Button _fill;
    private readonly Dictionary<string, Func<string>> _answers = new();
    private readonly List<ChatTurn> _turns = new();
    private CancellationTokenSource? _cancel;
    private string _context = string.Empty;
    private string _notes = string.Empty;
    private string _steps = string.Empty;

    /// <summary>The choice being worked out (registry entry, probe, documentation, explanation).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    /// <summary>The plan to fill in, once the person pressed Fill in the settings.</summary>
    public SetupPlan? Plan { get; private set; }

    /// <summary>The person's answers, by question key.</summary>
    public Dictionary<string, string> Answers { get; } = new();

    public McpHelperWindow(AssistantSettings settings)
    {
        _settings = settings;
        Title = "MCP Helper";
        Help.BindF1(this, () => Help.McpHelper);
        Width = 860;
        Height = 760;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = UiKit.Res<FontFamily>("UiFont");
        FontSize = 13;
        UseLayoutRounding = true;
        Background = UiKit.Brush("Surface");
        Foreground = UiKit.Brush("Ink");

        // Padding on a border rather than a margin, so the content starts at the window's edge (and pictures of it aren't cut).
        var root = new DockPanel();

        var top = new StackPanel();
        top.Children.Add(UiKit.Text("MCP Helper", "H2"));
        var intro = Note("Pick an archive below, or tell me which MCP server you want to add.  I look it up in the official MCP Registry, check how it signs in, explain what to do in that service, ask for the details I need, and fill in the settings for you.");
        intro.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(intro);
        var queryRow = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        _lookUp = UiKit.Button("PrimaryButton", "Look up", async (_, _) => await LookUpAsync());
        _lookUp.MinWidth = 90;
        _lookUp.Margin = new Thickness(8, 0, 0, 0);
        _lookUp.IsDefault = true;
        DockPanel.SetDock(_lookUp, Dock.Right);
        queryRow.Children.Add(_lookUp);
        _query = new TextBox { AcceptsReturn = false };
        queryRow.Children.Add(_query);
        top.Children.Add(queryRow);
        top.Children.Add(Note("A name (Open Archives, Gramps Web...), the server's web address, its documentation page, or a configuration snippet from its instructions (the part with \"mcpServers\")."));
        top.Children.Add(new Border { Height = 10 });
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var close = UiKit.Button("SecondaryButton", "Close", (_, _) => Close());
        close.MinWidth = 80;
        close.Margin = new Thickness(8, 0, 0, 0);
        close.IsCancel = true;
        _fill = UiKit.Button("PrimaryButton", "Fill in the settings", (_, _) => FillIn());
        _fill.MinWidth = 150;
        _fill.IsEnabled = false;
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_fill, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_fill);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Foreground = UiKit.Brush("Muted") };
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // Asking about the steps: on show above the buttons once the steps are written, with a few ready-made
        // questions; Enter asks, Shift+Enter starts a new line.
        var askHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        askHeader.Children.Add(new TextBlock { Text = "Ask the helper", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var askHint = Note("  Anything unclear in the steps, or a step that didn't work as described.");
        askHint.VerticalAlignment = VerticalAlignment.Center;
        askHeader.Children.Add(askHint);
        _askPanel.Children.Add(askHeader);
        foreach (string suggestion in new[] { "Explain the steps more simply", "Where do I find what I need to enter?", "What if signing in doesn't work?" })
        {
            var chip = UiKit.Button("SecondaryButton", suggestion, async (_, _) => await AskAsync(suggestion));
            chip.Padding = new Thickness(8, 2, 8, 2);
            chip.Margin = new Thickness(0, 0, 6, 4);
            chip.FontSize = 12;
            _suggestions.Children.Add(chip);
        }
        _askPanel.Children.Add(_suggestions);
        var askRow = new DockPanel();
        _ask = UiKit.Button("SecondaryButton", "Ask", async (_, _) => await AskAsync());
        _ask.MinWidth = 80;
        _ask.Margin = new Thickness(8, 0, 0, 0);
        _ask.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(_ask, Dock.Right);
        askRow.Children.Add(_ask);
        _followUp = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 46,
            MaxHeight = 110,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Enter asks; Shift+Enter starts a new line"
        };
        _followUp.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await AskAsync();
            }
        };
        askRow.Children.Add(_followUp);
        _askPanel.Children.Add(askRow);
        _askPanel.Children.Insert(0, new Border { Height = 1, Margin = new Thickness(0, 0, 0, 8), Background = UiKit.Brush("Border") });
        DockPanel.SetDock(_askPanel, Dock.Bottom);
        root.Children.Add(_askPanel);

        var body = new StackPanel();
        body.Children.Add(BuildWorkingPanel());
        body.Children.Add(BuildGenealogyList());
        _matchesPanel.Children.Add(new TextBlock { Text = "Which one do you mean?", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _matches = new ListBox { MaxHeight = 170, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = UiKit.Brush("SurfaceAlt") };
        _matches.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<StackPanel Margin='2,3'><TextBlock><Run Text='{Binding DisplayName, Mode=OneWay}' FontWeight='SemiBold'/><Run Text='  ·  '/><Run Text='{Binding Kind, Mode=OneWay}'/><Run Text='  ·  '/><Run Text='{Binding Name, Mode=OneWay}' Foreground='Gray'/></TextBlock>" +
            "<TextBlock Text='{Binding Description}' TextWrapping='Wrap' Opacity='0.8' FontSize='12'/></StackPanel></DataTemplate>");
        _matches.SelectionChanged += async (_, _) =>
        {
            if (_matches.SelectedItem is RegistryServer server)
                await (Working = ChooseAsync(server));
        };
        _matchesPanel.Children.Add(_matches);
        body.Children.Add(_matchesPanel);
        // "Instructions" with Copy instructions at its right, over the notes and steps.
        _copy = UiKit.Button("SecondaryButton", "Copy instructions", (_, _) => CopyInstructions());
        _copy.Padding = new Thickness(10, 2, 10, 2);
        _copy.ToolTip = "Copy the notes, the steps and your questions with their answers, formatted, to paste into an email or a document";
        DockPanel.SetDock(_copy, Dock.Right);
        _explainHeader.Children.Add(_copy);
        _explainHeader.Children.Add(new TextBlock { Text = "Instructions", Style = UiKit.Res<Style>("H3"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(_explainHeader);
        body.Children.Add(_explain);
        body.Children.Add(_questionsPanel);
        body.Children.Add(_conversation);

        _scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        root.Children.Add(_scroll);
        Content = new Border { Padding = new Thickness(20, 16, 20, 16), Child = root };
        Loaded += (_, _) => _query.Focus();
        Closed += (_, _) => _cancel?.Cancel();
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = UiKit.Brush("Muted") };

    private bool HasModel => _settings.HasKey;

    // ---- The genealogy list --------------------------------------------------------------------------------

    /// <summary>
    /// The archives' own servers to set up with one button, and the others with the reason each isn't offered
    /// (GenealogyServers, TDD 22.9).  Hidden once a server is being set up, shown again by a new look-up.
    /// </summary>
    private readonly StackPanel _genealogy = new() { Margin = new Thickness(0, 0, 0, 12) };

    private FrameworkElement BuildGenealogyList()
    {
        _genealogy.Children.Add(UiKit.Text("Genealogy archives", "H3"));
        var offeredNote = Note("Run by the archives themselves.  Searches you make through them go to that archive, as they would on its website.");
        offeredNote.Margin = new Thickness(0, 2, 0, 8);
        _genealogy.Children.Add(offeredNote);
        foreach (var server in GenealogyServers.All.Where(s => s.Offered))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var setUp = UiKit.Button("SecondaryButton", "Set up", async (_, _) => await (Working = ChooseGenealogyAsync(server)));
            setUp.VerticalAlignment = VerticalAlignment.Top;
            setUp.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(setUp, Dock.Right);
            row.Children.Add(setUp);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{server.Name}  ·  {server.Coverage}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(Note(server.Description));
            if (server.Note != null)
                text.Children.Add(Note(server.Note));
            row.Children.Add(text);
            _genealogy.Children.Add(row);
        }
        var notOffered = new Expander { Header = "Not offered, and why", Margin = new Thickness(0, 2, 0, 0) };
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var server in GenealogyServers.All.Where(s => !s.Offered))
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            item.Children.Add(new TextBlock { Text = $"{server.Name}  ·  {server.Coverage}  ·  run by {server.RunBy}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            item.Children.Add(Note(server.Description));
            item.Children.Add(Note(server.Reason ?? string.Empty));
            list.Children.Add(item);
        }
        notOffered.Content = list;
        _genealogy.Children.Add(notOffered);
        return _genealogy;
    }

    /// <summary>An archive from the genealogy list: its address, asked how it signs in, then the usual steps.</summary>
    public async Task ChooseGenealogyAsync(GenealogyServer server)
    {
        if (server.Url is not { } url)
            return;
        var cancel = Restart();
        Reset();
        _query.Text = server.Name;
        using var work = Busy($"Getting {server.Name} ready…");
        try
        {
            using var http = McpOAuth.NewHttp();
            var plan = SetupPlan.FromUrl(url);
            plan.Name = server.Name;
            plan.Source = "Mycelia's genealogy list";
            plan.Notes.Insert(0, server.Description);
            if (server.Note != null)
                plan.Notes.Insert(1, server.Note);
            Status("Checking how " + new Uri(url).Host + " signs in…");
            plan.Apply(await McpProbe.ProbeAsync(http, url, cancel));
            await ShowAsync(plan, $"The person chose {server.Name} ({server.Coverage}) from Mycelia's genealogy list. {server.Description} {server.Note}", null, cancel);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- Working: the mark, pulsing, while the helper looks up, checks, reads and writes ---------

    private readonly Border _workingPanel = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
    private readonly TextBlock _workingText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly Image _workingPicture = new() { Width = 40, Height = 40, RenderTransformOrigin = new Point(0.5, 0.5) };
    private int _busy;

    private FrameworkElement BuildWorkingPanel()
    {
        _workingPanel.Background = UiKit.Brush("BrandSoft");
        _workingPanel.BorderBrush = UiKit.Brush("Border");
        _workingPicture.Source = UiKit.Res<ImageSource>("MarkInkImage");
        _workingPicture.RenderTransform = _workingScale;
        // The animation runs only while the panel is on show.
        _workingPanel.IsVisibleChanged += (_, e) => Pulse((bool)e.NewValue);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        words.Children.Add(_workingText);
        words.Children.Add(Note("Please wait: I'm working on it.  This can take up to a minute."));
        var row = new DockPanel();
        DockPanel.SetDock(_workingPicture, Dock.Left);
        row.Children.Add(_workingPicture);
        row.Children.Add(words);
        _workingPanel.Child = row;
        return _workingPanel;
    }

    private readonly ScaleTransform _workingScale = new(1, 1);

    /// <summary>
    /// Starts or stops the pulse: slightly larger and brighter, then back, once a second.  Windows set to show fewer
    /// animations keeps the picture still.
    /// </summary>
    private void Pulse(bool on)
    {
        if (!on || !SystemParameters.ClientAreaAnimation)
        {
            _workingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _workingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _workingPicture.BeginAnimation(OpacityProperty, null);
            return;
        }
        var grow = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1.08, TimeSpan.FromMilliseconds(500))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = new System.Windows.Media.Animation.SineEase() };
        _workingScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _workingScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        _workingPicture.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(500))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
    }

    /// <summary>Says what the helper is doing: in the working panel while it works, else in the status line.</summary>
    private void Status(string text)
    {
        _status.Text = text;
        if (_busy > 0 && text.Length > 0)
            _workingText.Text = text;
    }

    /// <summary>Starts a piece of work: the panel shows, Look up and Fill in wait.  Disposing it ends the work.</summary>
    private IDisposable Busy(string text)
    {
        _busy++;
        _workingText.Text = text;
        _workingPanel.Visibility = Visibility.Visible;
        _status.Text = string.Empty;
        _lookUp.IsEnabled = false;
        _fill.IsEnabled = false;
        // The helper stays usable (Close, scrolling) while it works, so the arrow with the hourglass.
        Cursor = Cursors.AppStarting;
        _workingPanel.BringIntoView();
        return new EndOfWork(this);
    }

    private sealed class EndOfWork(McpHelperWindow window) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            if (--window._busy > 0)
                return;
            window._workingPanel.Visibility = Visibility.Collapsed;
            window.Cursor = null;
            // A progress message ("Checking…") left over says nothing once the work is done.
            if (window._status.Text.EndsWith('…'))
                window._status.Text = string.Empty;
            window._lookUp.IsEnabled = true;
            window._fill.IsEnabled = window._plan is { } plan && (plan.Url.Length > 0 || plan.Command.Length > 0);
        }
    }

    private CancellationToken Restart()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        return _cancel.Token;
    }

    // ---- Looking up ------------------------------------------------------------------------------------

    /// <summary>Looks a server up as if typed.</summary>
    public Task LookUpAsync(string query)
    {
        _query.Text = query;
        return LookUpAsync();
    }

    private async Task LookUpAsync()
    {
        string query = _query.Text.Trim();
        if (query.Length == 0)
        {
            Status("Type a server's name, address or configuration first, or pick an archive from the list.");
            return;
        }
        var cancel = Restart();
        Reset();
        using var work = Busy("Looking up " + query + "…");
        try
        {
            using var http = McpOAuth.NewHttp();
            if (query.StartsWith('{') || query.Contains("\"mcpServers\"") || query.Contains("\"command\"") || query.Contains("\"url\""))
            {
                var plan = SetupPlan.FromConfig(query);
                if (plan == null)
                {
                    Status("That configuration couldn't be read: paste the whole \"mcpServers\" part, braces included.");
                    return;
                }
                if (plan.Transport == McpTransportKind.Http && plan.Url.Length > 0 && !plan.Url.Contains('{'))
                {
                    Status("Checking how " + plan.Url + " signs in…");
                    plan.Apply(await McpProbe.ProbeAsync(http, plan.Url, cancel));
                }
                await ShowAsync(plan, "The person pasted this configuration:\n" + Shorten(query, 4000), null, cancel);
                return;
            }

            if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                Status("Checking " + uri.Host + "…");
                var probe = await McpProbe.ProbeAsync(http, query, cancel);
                if (probe.Kind != ProbeKind.NotMcp && probe.Kind != ProbeKind.Unreachable)
                {
                    var plan = SetupPlan.FromUrl(query);
                    plan.Apply(probe);
                    await ShowAsync(plan, $"The person gave the server's address {query}. Mycelia's check: {probe.Kind}: {probe.Detail}", null, cancel);
                    return;
                }
                // A documentation page: read it, and let the model find the server's address in it.
                Status("Reading " + uri.Host + "…");
                string? docs = await McpDocs.FetchAsync(http, query, cancel);
                await FromDocumentationAsync(http, query, docs, $"The person gave this page: {query} ({probe.Detail})", cancel);
                return;
            }

            Status("Looking up \"" + query + "\" in the MCP Registry…");
            List<RegistryServer> found;
            try
            {
                found = await McpRegistry.FindAsync(http, query, cancel);
            }
            catch (Exception ex) when (ex is McpException or HttpRequestException or System.Text.Json.JsonException)
            {
                found = new List<RegistryServer>();
                Status("The MCP Registry couldn't be reached (" + ex.Message + ").");
            }
            var ranked = McpRegistry.Rank(found, query);
            if (ranked.Count == 0)
            {
                await FromDocumentationAsync(http, null, null, $"The person asked for \"{query}\", which isn't in the MCP Registry.", cancel);
                return;
            }
            _matches.ItemsSource = ranked;
            _matchesPanel.Visibility = ranked.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Status(ranked.Count > 1 ? $"{ranked.Count} servers in the MCP Registry match.  The closest is chosen; pick another if it isn't the one." : string.Empty);
            _matches.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ChooseAsync(RegistryServer server)
    {
        var cancel = Restart();
        using var work = Busy($"Getting {server.DisplayName} ready…");
        try
        {
            using var http = McpOAuth.NewHttp();
            var plan = SetupPlan.FromRegistry(server);
            string company = _query.Text.Trim();
            if (company.Length > 0 && company.All(c => char.IsLetterOrDigit(c) || c is '-' or ' ') && !server.IsFrom(company))
                plan.Notes.Insert(0, $"Published by {server.Publisher}, not by {company} itself: your searches go through their service, so make sure you trust them.  Choose another entry if there's one from {company}.");
            if (plan.Transport == McpTransportKind.Http && plan.Url.Length > 0 && !plan.Url.Contains('{'))
            {
                Status("Checking how " + new Uri(plan.Url).Host + " signs in…");
                plan.Apply(await McpProbe.ProbeAsync(http, plan.Url, cancel));
            }
            Status("Reading its documentation…");
            var docs = new StringBuilder();
            foreach (var url in new[] { server.WebsiteUrl, McpDocs.ReadmeUrl(server.RepositoryUrl, server.RepositorySubfolder) }.Where(u => u != null).Distinct())
            {
                if (await McpDocs.FetchAsync(http, url!, cancel) is { Length: > 0 } text)
                    docs.Append("Documentation from ").Append(url).AppendLine(":").AppendLine(text).AppendLine();
            }
            string entry = $"MCP Registry entry {server.Name} {server.Version}: {server.DisplayName}. {server.Description}" +
                           (server.WebsiteUrl != null ? $" Website: {server.WebsiteUrl}." : string.Empty) +
                           (server.RepositoryUrl != null ? $" Repository: {server.RepositoryUrl}." : string.Empty);
            await ShowAsync(plan, entry, docs.Length > 0 ? docs.ToString() : null, cancel);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// No registry entry or a documentation page: the model (when set up) reads what there is and names the server's
    /// address on an "ADDRESS:" line, which is only used once the address answers as an MCP server.
    /// </summary>
    private async Task FromDocumentationAsync(HttpClient http, string? page, string? docs, string what, CancellationToken cancel)
    {
        if (!HasModel)
        {
            _notes = "I couldn't find this server in the MCP Registry" + (page != null ? " or at that address" : string.Empty) + ".  " +
                     "Give its MCP server address (often ending in /mcp), or paste the configuration from its instructions.  " +
                     "With an AI model set up (Options › AI model) I can also read its documentation for you.";
            _explain.Markdown = _notes;
            Status(string.Empty);
            return;
        }
        Status("Finding out what I can about it…");
        string prompt = what + (docs != null ? "\n\nThe page says:\n" + docs : string.Empty) +
                        "\n\nIf you know or the page says the MCP server's address (Streamable HTTP, often ending in /mcp), put it alone on the first line as 'ADDRESS: https://...'; otherwise write 'ADDRESS: unknown'. " +
                        "If it's a program to run instead, put the command on the first line as 'COMMAND: npx -y package' or similar. Then explain as usual.";
        string answer = await AskModelAsync(prompt, cancel);
        var address = Regex.Match(answer, @"^ADDRESS:\s*(https?://\S+)", RegexOptions.Multiline);
        var command = Regex.Match(answer, @"^COMMAND:\s*(.+)$", RegexOptions.Multiline);
        string text = Regex.Replace(answer, @"^(ADDRESS|COMMAND):.*\r?\n?", string.Empty, RegexOptions.Multiline).Trim();
        SetupPlan? plan = null;
        if (address.Success)
        {
            string url = address.Groups[1].Value.TrimEnd('.', ')');
            var probe = await McpProbe.ProbeAsync(http, url, cancel);
            if (probe.Kind is not (ProbeKind.NotMcp or ProbeKind.Unreachable))
            {
                plan = SetupPlan.FromUrl(url);
                plan.Apply(probe);
                plan.Notes.Insert(0, "The AI model named this address, and it answers as an MCP server.");
            }
            else
            {
                text = $"(The address the AI model suggested, {url}, didn't answer as an MCP server: {probe.Detail})\n\n" + text;
            }
        }
        else if (command.Success)
        {
            var parts = command.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            plan = new SetupPlan { Name = _query.Text.Trim(), Source = "Suggested by the AI model; check it against the documentation", Transport = McpTransportKind.Stdio, Command = parts[0] };
            plan.Arguments.AddRange(parts.Skip(1));
            plan.Notes.Add($"Runs on this computer: {command.Groups[1].Value.Trim()}.");
            plan.Notes.Add(SetupPlan.Prerequisite(parts[0]));
        }
        if (plan != null)
        {
            ShowPlan(plan);
            _steps = text;
            Render();
        }
        else
        {
            _explain.Markdown = text;
            Status("I couldn't find an address that answers as an MCP server: give it (or a configuration snippet) above.");
        }
        _askPanel.Visibility = Visibility.Visible;
    }

    // ---- Showing the plan ---------------------------------------------------------------------------------

    private void Reset()
    {
        Plan = null;
        _fill.IsEnabled = false;
        _matches.ItemsSource = null;
        _matchesPanel.Visibility = Visibility.Collapsed;
        _questionsPanel.Children.Clear();
        _questionsPanel.Visibility = Visibility.Collapsed;
        _askPanel.Visibility = Visibility.Collapsed;
        _explainHeader.Visibility = Visibility.Collapsed;
        _explain.Markdown = string.Empty;
        _conversation.Children.Clear();
        _asked.Clear();
        _turns.Clear();
        _answers.Clear();
        _notes = _steps = string.Empty;
        _genealogy.Visibility = Visibility.Collapsed;
    }

    private SetupPlan? _plan;

    private async Task ShowAsync(SetupPlan plan, string context, string? docs, CancellationToken cancel)
    {
        ShowPlan(plan);
        _context = context + "\n\nWhat Mycelia will fill in:\n" + plan.Describe() + (docs != null ? "\n\n" + docs : string.Empty);
        _turns.Clear();
        if (!HasModel)
        {
            _steps = "Set up an AI model in Options › AI model and I'll also explain, step by step, what to do in the service.";
            Render();
            Status(plan.Questions.Count > 0 ? "Answer the questions below, then press Fill in the settings." : "Press Fill in the settings when you're ready.");
            return;
        }
        Status("Writing the steps for you…");
        _steps = "I'm writing them now.  They'll appear here in a moment.";
        Render();
        _steps = await AskModelAsync(_context, cancel);
        Render();
        _askPanel.Visibility = Visibility.Visible;
        Status(plan.Questions.Count > 0 ? "Answer the questions below, then press Fill in the settings." : "Press Fill in the settings when you're ready.");
    }

    private void ShowPlan(SetupPlan plan)
    {
        _plan = plan;
        var notes = new StringBuilder();
        notes.Append("**").Append(plan.Name).Append("** · ").Append(plan.Source).AppendLine().AppendLine();
        foreach (string note in plan.Notes)
            notes.Append("- ").AppendLine(note);
        if (plan.BrowserSignIn)
            notes.Append("- Callback address (register it with your own app if you need one): `").Append(McpOAuthSettings.DefaultCallback).AppendLine("`");
        _notes = notes.ToString();
        BuildQuestions(plan);
        // While the helper is still working, Fill in waits for it (the end of the work turns it on).
        if (_busy == 0)
            _fill.IsEnabled = plan.Url.Length > 0 || plan.Command.Length > 0;
        Render();
    }

    private void Render()
    {
        _explain.Markdown = _notes + (_steps.Length > 0 ? "\n**What to do**\n\n" + _steps : string.Empty);
        _explainHeader.Visibility = _notes.Length > 0 || _steps.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Copy instructions: the server's notes, the steps and every question with its answer, as Markdown turned into HTML
    /// and plain text (ChatText.CopyFormatted, as the Assistant's Copy button), to keep as a reference elsewhere.
    /// </summary>
    private void CopyInstructions()
    {
        var text = new StringBuilder();
        text.Append("**Setting up ").Append(_plan?.Name ?? "the MCP server").AppendLine(" in Mycelia**").AppendLine();
        text.Append(_notes);
        if (_steps.Length > 0)
            text.AppendLine().AppendLine("**What to do**").AppendLine().AppendLine(_steps);
        foreach (var (question, answer) in _asked)
            text.AppendLine().Append("**Question:** ").AppendLine(question).AppendLine().AppendLine(answer);
        try
        {
            ChatText.CopyFormatted(text.ToString());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status("The clipboard is busy; try again.");
            return;
        }
        _copy.Content = "Copied";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { _copy.Content = "Copy instructions"; timer.Stop(); };
        timer.Start();
        Status("Copied the instructions; paste them into an email or a document.");
    }

    /// <summary>A question in the conversation: on the right, in the brand's soft colour.</summary>
    private static Border QuestionBubble(string question) => new()
    {
        Child = new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
        Padding = new Thickness(10, 6, 10, 6),
        CornerRadius = new CornerRadius(8, 8, 2, 8),
        HorizontalAlignment = HorizontalAlignment.Right,
        MaxWidth = 560,
        Margin = new Thickness(60, 8, 0, 4),
        Background = UiKit.Brush("BrandSoft"),
    };

    /// <summary>An answer in the conversation: on the left, in a bordered card, with simple Markdown.</summary>
    private static Border AnswerBubble(string markdown) => new()
    {
        Child = new ChatText(markdown),
        Padding = new Thickness(10, 6, 10, 4),
        CornerRadius = new CornerRadius(8, 8, 8, 2),
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 60, 4),
        Background = UiKit.Brush("SurfaceAlt"),
        BorderBrush = UiKit.Brush("Border"),
    };

    private void ScrollToEnd() => Dispatcher.BeginInvoke(_scroll.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Loaded);

    private void BuildQuestions(SetupPlan plan)
    {
        _questionsPanel.Children.Clear();
        _answers.Clear();
        if (plan.Questions.Count == 0)
        {
            _questionsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        _questionsPanel.Visibility = Visibility.Visible;
        _questionsPanel.Children.Add(new TextBlock { Text = "Details to enter", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) });
        foreach (var question in plan.Questions)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = question.Label + (question.Required ? "" : " (optional)"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 8, 0), Foreground = UiKit.Brush("Muted") });
            var field = new StackPanel();
            if (question.Secret)
            {
                var box = new PasswordBox { Password = question.Default ?? string.Empty };
                _answers[question.Key] = () => box.Password;
                field.Children.Add(box);
            }
            else
            {
                var box = new TextBox { Text = question.Default ?? string.Empty };
                _answers[question.Key] = () => box.Text;
                field.Children.Add(box);
            }
            if (question.Help != null)
            {
                var help = Note(question.Help + (question.Secret ? "  Kept encrypted." : string.Empty));
                help.Margin = new Thickness(0, 2, 0, 0);
                field.Children.Add(help);
            }
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            _questionsPanel.Children.Add(grid);
        }
    }

    // ---- The AI model -------------------------------------------------------------------------------------

    private async Task<string> AskModelAsync(string message, CancellationToken cancel)
    {
        var options = _settings;
        // Reasoning models (gpt-5, o-series) spend part of the allowance thinking, so at least 8000 tokens.
        var provider = ChatProviders.Create(new ChatProviderOptions(options.Provider, options.Model, options.GetApiKey(options.Provider), options.Endpoint, Math.Max(8000, options.MaxTokens)));
        _turns.Add(new UserTurn(message));
        try
        {
            var reply = await provider.CompleteAsync(McpDocs.ExplainPrompt, _turns, Array.Empty<ToolSpec>(), cancel);
            _turns.Add(new AssistantTurn(reply.Text, Array.Empty<ToolCall>()));
            Status(string.Empty);
            if (reply.Text.Trim().Length == 0)
                return reply.StopReason is "length" or "max_tokens"
                    ? "The AI model ran out of room before answering.  Raise the answer length in Options › AI model, or ask again below."
                    : "The AI model gave no answer; ask again below.";
            return reply.Text.Trim();
        }
        catch (ChatException ex)
        {
            _turns.RemoveAt(_turns.Count - 1);
            Status("The AI model couldn't be asked: " + ex.Message);
            return "The AI model couldn't be asked; the notes above still apply.";
        }
    }

    private bool _asking;

    /// <summary>
    /// Asks the AI model about the steps: the question and, once it comes, the answer join the conversation under the
    /// steps (the steps themselves stay as written), with "Thinking…" in its place meanwhile.
    /// </summary>
    private async Task AskAsync(string? asked = null)
    {
        string question = (asked ?? _followUp.Text).Trim();
        if (question.Length == 0 || !HasModel || _turns.Count == 0 || _asking)
            return;
        _asking = true;
        if (asked == null)
            _followUp.Clear();
        _ask.IsEnabled = false;
        _ask.Content = "Thinking…";
        _suggestions.IsEnabled = false;
        _conversation.Children.Add(QuestionBubble(question));
        var thinking = AnswerBubble("Thinking about your question…");
        _conversation.Children.Add(thinking);
        ScrollToEnd();
        try
        {
            string answer = await AskModelAsync(question, _cancel?.Token ?? CancellationToken.None);
            _conversation.Children.Remove(thinking);
            _conversation.Children.Add(AnswerBubble(answer));
            _asked.Add((question, answer));
            ScrollToEnd();
        }
        finally
        {
            _asking = false;
            _ask.IsEnabled = true;
            _ask.Content = "Ask";
            _suggestions.IsEnabled = true;
            _followUp.Focus();
        }
    }

    // ---- Filling in ---------------------------------------------------------------------------------------------

    private void FillIn()
    {
        if (_plan is not { } plan)
            return;
        Answers.Clear();
        foreach (var (key, read) in _answers)
            Answers[key] = read();
        var missing = plan.Missing(Answers);
        if (missing.Count > 0)
        {
            Status("Still needed: " + string.Join(", ", missing.Select(q => q.Label)) + ".");
            return;
        }
        Plan = plan;
        DialogResult = true;
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
