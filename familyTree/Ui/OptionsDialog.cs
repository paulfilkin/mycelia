using System.Windows;
using System.Windows.Controls;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>
/// Tools > Options: the program settings, in tabs (General, Dates and names,
/// Warnings, TDD 17), then this app's own tabs for
/// the Assistant and its MCP servers (OptionsDialog.Ai.cs, TDD 22).
/// </summary>
public sealed partial class OptionsDialog : Window
{
    public static readonly string[] Workspaces = ["Home", "People", "Places", "Sources", "Media", "Charts", "Tools"];

    private readonly AppSettings _settings;
    private readonly ComboBox _startWorkspace = new() { ItemsSource = Workspaces, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _reopen = new() { Content = "Open the most recent tree when starting" };
    private readonly ComboBox _dateStyle = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "Text" };
    private readonly RadioButton _dayFirst = new() { Content = "Day Month Year (3/4/1890 is 3 April)", GroupName = "input" };
    private readonly RadioButton _monthFirst = new() { Content = "Month Day Year (3/4/1890 is 4 March)", GroupName = "input" };
    private readonly CheckBox _caps = new() { Content = "Show surnames in capitals" };
    private readonly CheckBox _warnings = new() { Content = "Show alerts for unlikely birth, death and marriage dates" };

    public OptionsDialog(AppSettings settings, Ai.Mcp.McpHub? hub = null, IReadOnlyList<Ai.Assistant.AssistantTool>? builtInTools = null, string? page = null)
    {
        _settings = settings;
        _hub = hub;
        _builtInTools = builtInTools ?? [];
        Title = "Options";
        Width = 920;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var owner = Application.Current.MainWindow;
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        Background = UiKit.Brush("Surface");
        FontFamily = UiKit.Res<System.Windows.Media.FontFamily>("UiFont");
        FontSize = 13;
        Foreground = UiKit.Brush("Ink");
        UseLayoutRounding = true;

        var sample = GenDate.Parse("5 Mar 1890");
        _dateStyle.ItemsSource = Enum.GetValues<DateStyle>().Select(s => new { Style = s, Text = sample.Format(s) }).ToList();

        // Current values
        _startWorkspace.SelectedItem = Workspaces.Contains(settings.StartWorkspace) ? settings.StartWorkspace : "People";
        _reopen.IsChecked = settings.ReopenLastTree;
        _dateStyle.SelectedIndex = (int)settings.DateStyle;
        _dayFirst.IsChecked = settings.DayFirst;
        _monthFirst.IsChecked = !settings.DayFirst;
        _caps.IsChecked = settings.SurnamesInCapitals;
        _warnings.IsChecked = settings.ShowDataWarnings;

        var tabs = new TabControl { Style = UiKit.Res<Style>("PanelTabs"), Height = 580 };
        tabs.Items.Add(new TabItem { Header = "General", Content = Page(
            Label("Start in this workspace"), _startWorkspace, Gap(),
            _reopen) });
        tabs.Items.Add(new TabItem { Header = "Dates and names", Content = Page(
            Label("Date display format"), _dateStyle, Gap(),
            Label("Date input format, for dates typed with numbers"), _dayFirst, _monthFirst, Gap(),
            Label("Names"), _caps,
            Note("Dates are always kept as typed; these settings change how they are read and shown.")) });
        tabs.Items.Add(new TabItem { Header = "Warnings", Content = Page(
            _warnings,
            Note($"The checks expect parents to be at least {DataChecker.ParentMinimumAge}, mothers up to {DataChecker.MotherMaximumAge}, fathers up to {DataChecker.FatherMaximumAge}, and ages at death up to {DataChecker.QuestionableDeathAge}.  Tools > Find data errors works either way.")) });
        AddAiTabs(tabs);
        AddResearchTab(tabs);
        AddServingTab(tabs);
        AddUpdatesTab(tabs);
        if (page is not null && tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Header == page) is { } start)
            tabs.SelectedItem = start;
        // F1: the help for the tab in front.
        Help.BindF1(this, () => Help.ForOptionsTab((tabs.SelectedItem as TabItem)?.Header as string));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Style = UiKit.Res<Style>("SecondaryButton"), IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", Style = UiKit.Res<Style>("PrimaryButton"), IsDefault = true, MinWidth = 96 };
        ok.Click += (_, _) => { if (Apply()) DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        root.Children.Add(UiKit.Text("Options", "H2"));
        var border = new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 0), Child = tabs };
        root.Children.Add(border);
        root.Children.Add(buttons);
        Content = root;
    }

    private static TextBlock Label(string text) => new() { Text = text, Style = UiKit.Res<Style>("FieldLabel") };

    private static FrameworkElement Gap() => new Border { Height = 14 };

    private static TextBlock Note(string text)
    {
        var t = UiKit.Text(text, "Caption");
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        t.Margin = new Thickness(0, 14, 0, 0);
        return t;
    }

    private static FrameworkElement Page(params UIElement[] items)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        foreach (var item in items)
        {
            if (item is CheckBox or RadioButton) ((FrameworkElement)item).Margin = new Thickness(0, 2, 0, 6);
            sp.Children.Add(item);
        }
        return sp;
    }

    private bool Apply()
    {
        if (!ApplyAi()) return false;
        if (!ApplyServing()) return false;
        ApplyResearch();
        ApplyUpdates();
        _settings.StartWorkspace = _startWorkspace.SelectedItem as string ?? "People";
        _settings.ReopenLastTree = _reopen.IsChecked == true;
        _settings.DateStyle = (DateStyle)Math.Max(0, _dateStyle.SelectedIndex);
        _settings.DayFirst = _dayFirst.IsChecked == true;
        _settings.SurnamesInCapitals = _caps.IsChecked == true;
        _settings.ShowDataWarnings = _warnings.IsChecked == true;
        _settings.Save();
        ApplyToApp(_settings);
        return true;
    }

    /// <summary>Puts the display settings into effect (at start-up and after OK).</summary>
    public static void ApplyToApp(AppSettings settings)
    {
        GenDate.Style = settings.DateStyle;
        GenDate.DayFirst = settings.DayFirst;
        Person.SurnamesInCapitals = settings.SurnamesInCapitals;
        Views.PersonEditor.ShowWarnings = settings.ShowDataWarnings;
    }
}
