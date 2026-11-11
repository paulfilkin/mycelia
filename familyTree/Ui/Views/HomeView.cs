using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>Start page: the tree at a glance, recent files, preferences.</summary>
public sealed class HomeView : UserControl, IWorkspace
{
    private AppState? _state;
    private AppSettings? _settings;
    private MainWindow? _window;

    public void Attach(AppState state, AppSettings settings, MainWindow window)
    {
        _state = state;
        _settings = settings;
        _window = window;
        state.DocumentReplaced += Build;
        state.TreeChanged += () => { if (IsVisible) Build(); };
        Build();
    }

    public void OnShown() => Build();

    private void Build()
    {
        if (_state is null) return;
        var doc = _state.Doc;
        var page = new StackPanel { Margin = new Thickness(36, 28, 36, 36), MaxWidth = 1180 };

        // The wordmark for light backgrounds and the tagline, from the artwork sheet
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
        brand.Children.Add(new Image { Source = UiKit.Res<System.Windows.Media.ImageSource>("MarkInkImage"), Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center });
        var wordmark = UiKit.Text("mycelia", "H1");
        wordmark.FontSize = 24;
        wordmark.VerticalAlignment = VerticalAlignment.Center;
        wordmark.Margin = new Thickness(10, -4, 14, 0);
        brand.Children.Add(wordmark);
        var tagline = UiKit.Text("Family trees, connected to their sources.", "Caption");
        tagline.FontSize = 13;
        tagline.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(tagline);
        page.Children.Add(brand);

        // Title and tree name
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(UiKit.Button("SecondaryButton", "New tree", (_, _) => _window!.NewTree(), "Plus"));
        actions.Children.Add(Gap(UiKit.Button("SecondaryButton", "Open...", (_, _) => _window!.OpenFile(), "Open")));
        actions.Children.Add(Gap(UiKit.Button("SecondaryButton", "Import GEDCOM...", (_, _) => _window!.ImportGedcom(), "Import")));
        actions.Children.Add(Gap(UiKit.Button("PrimaryButton", "Go to people", (_, _) => _state.Navigate("People"), "People")));
        DockPanel.SetDock(actions, Dock.Right);
        titleRow.Children.Add(actions);
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Text("YOUR TREE", "SectionLabel"));
        var nameBox = new TextBox
        {
            Text = doc.Data.Name,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Click to rename the tree",
        };
        var token = new object();
        nameBox.GotKeyboardFocus += (_, _) => token = new object();
        nameBox.TextChanged += (_, _) =>
        {
            if (nameBox.Text == doc.Data.Name) return;
            doc.BeginEdit(token);
            doc.Data.Name = nameBox.Text;
            doc.MarkEdited();
        };
        titles.Children.Add(nameBox);
        titles.Children.Add(UiKit.Text(doc.FilePath ?? "Not saved yet", "Caption"));
        titleRow.Children.Add(titles);
        page.Children.Add(titleRow);

        // Statistics
        var c = doc.Counts();
        var stats = new UniformGrid { Columns = 6, Margin = new Thickness(-6, 0, -6, 18) };
        stats.Children.Add(Stat("People", c.People.ToString("N0"), "People", "People"));
        stats.Children.Add(Stat("Families", c.Families.ToString("N0"), "Rings", "People"));
        stats.Children.Add(Stat("Surnames", c.Surnames.ToString("N0"), "Person", "People"));
        stats.Children.Add(Stat("Places", c.Places.ToString("N0"), "Pin", "Places"));
        stats.Children.Add(Stat("Sources", c.Sources.ToString("N0"), "Book", "Sources"));
        stats.Children.Add(Stat("Media", c.Media.ToString("N0"), "Image", "Media"));
        page.Children.Add(stats);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(HomePersonCard(doc));
        left.Children.Add(Spacer(UiKit.Card(SurnamesPanel(doc))));
        columns.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(UiKit.Card(RecentPanel()));
        right.Children.Add(Spacer(UiKit.Card(PreferencesPanel())));
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        page.Children.Add(columns);

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static FrameworkElement Gap(FrameworkElement e)
    {
        e.Margin = new Thickness(8, 0, 0, 0);
        return e;
    }

    private static FrameworkElement Spacer(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 18, 0, 0);
        return e;
    }

    private FrameworkElement Stat(string label, string value, string icon, string workspace)
    {
        var sp = new StackPanel();
        var ic = UiKit.Icon(icon, 20, UiKit.Brush("Brand"));
        ic.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(ic);
        sp.Children.Add(UiKit.Text(value, "H1"));
        sp.Children.Add(UiKit.Text(label, "Caption"));
        var card = UiKit.Card(sp, new Thickness(18, 16, 18, 14));
        card.Margin = new Thickness(6, 0, 6, 0);
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => _state!.Navigate(workspace);
        return card;
    }

    private FrameworkElement HomePersonCard(TreeDocument doc)
    {
        var home = doc.HomePerson;
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("HOME PERSON", "SectionLabel"));
        if (home is null)
        {
            sp.Children.Add(UiKit.Text("Nobody yet. Add the first person in People.", "Caption"));
            return UiKit.Card(sp);
        }
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Avatar(doc, home, 64));
        var text = new StackPanel { Margin = new Thickness(16, 4, 0, 0) };
        text.Children.Add(UiKit.Text(home.DisplayName, "H2"));
        text.Children.Add(UiKit.Text(home.LifeSpan, "Caption"));
        var ancestors = CountAncestors(doc, home);
        var descendants = CountDescendants(doc, home);
        text.Children.Add(UiKit.Text($"{ancestors.Count} ancestors over {ancestors.Generations} generations   ·   {descendants} descendants", "Caption"));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var go = UiKit.Button("SecondaryButton", "Show in tree", (_, _) => { _state!.Select(home, focus: true); _state.Navigate("People"); }, "Tree");
        go.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(go, 2);
        row.Children.Add(go);
        sp.Children.Add(row);

        // Facts completeness
        var people = doc.Data.People;
        if (people.Count > 0)
        {
            var withBirth = people.Count(p => p.Birth?.ParsedDate.IsValid == true);
            var withSources = people.Count(p => p.Facts.Any(f => f.Citations.Count > 0));
            var bars = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            bars.Children.Add(Progress("People with a birth date", withBirth, people.Count));
            bars.Children.Add(Progress("People with at least one source", withSources, people.Count));
            sp.Children.Add(bars);
        }
        return UiKit.Card(sp);
    }

    private static FrameworkElement Progress(string label, int value, int total)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var head = new DockPanel();
        var pct = UiKit.Text($"{value} of {total}", "Caption");
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(UiKit.Text(label, size: 12.5));
        sp.Children.Add(head);
        var track = new Grid { Height = 6, Margin = new Thickness(0, 5, 0, 0) };
        track.Children.Add(new Border { Background = UiKit.Brush("NeutralSoft"), CornerRadius = new CornerRadius(3) });
        var fill = new Border { Background = UiKit.Brush("Brand"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        track.SizeChanged += (_, e) => fill.Width = total == 0 ? 0 : e.NewSize.Width * value / total;
        track.Children.Add(fill);
        sp.Children.Add(track);
        return sp;
    }

    private static (int Count, int Generations) CountAncestors(TreeDocument doc, Person p)
    {
        var seen = new HashSet<string>();
        var frontier = new List<Person> { p };
        var gens = 0;
        while (frontier.Count > 0 && gens < 60)
        {
            var next = frontier.SelectMany(doc.Parents).Where(x => seen.Add(x.Id)).ToList();
            if (next.Count == 0) break;
            gens++;
            frontier = next;
        }
        return (seen.Count, gens + 1);
    }

    private static int CountDescendants(TreeDocument doc, Person p)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<Person>();
        stack.Push(p);
        while (stack.Count > 0)
            foreach (var c in doc.Children(stack.Pop()))
                if (seen.Add(c.Id)) stack.Push(c);
        return seen.Count;
    }

    private FrameworkElement SurnamesPanel(TreeDocument doc)
    {
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("MOST COMMON SURNAMES", "SectionLabel"));
        var groups = doc.Data.People.Where(p => p.Surname.Length > 0)
            .GroupBy(p => p.Surname, StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(8).ToList();
        if (groups.Count == 0) sp.Children.Add(UiKit.Text("No surnames yet.", "Caption"));
        var max = groups.Count == 0 ? 1 : groups.Max(g => g.Count());
        foreach (var g in groups)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.Children.Add(UiKit.Text(g.Key, weight: FontWeights.Medium));
            var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = UiKit.Brush("BrandMid"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var holder = new Grid();
            holder.Children.Add(bar);
            var count = g.Count();
            holder.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * count / max;
            Grid.SetColumn(holder, 1);
            row.Children.Add(holder);
            var n = UiKit.Text(count.ToString(), "Caption");
            n.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(n, 2);
            row.Children.Add(n);
            sp.Children.Add(row);
        }
        return sp;
    }

    private FrameworkElement RecentPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("RECENT TREES", "SectionLabel"));
        var files = _settings!.RecentFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            var none = UiKit.Text("Trees you save or open appear here.", "Caption");
            none.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(none);
        }
        foreach (var f in files)
        {
            var b = new Button { Style = UiKit.Res<Style>("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6) };
            var content = new StackPanel();
            content.Children.Add(UiKit.Text(Path.GetFileNameWithoutExtension(f), weight: FontWeights.Medium, brush: UiKit.Brush("Ink")));
            content.Children.Add(UiKit.Text(Path.GetDirectoryName(f) ?? "", "Caption"));
            b.Content = content;
            var path = f;
            b.Click += (_, _) => { if (_window!.ConfirmDiscard()) _window.OpenPath(path); };
            sp.Children.Add(b);
        }
        return sp;
    }

    /// <summary>A summary of the main settings, with the button to the Options window where they are changed.</summary>
    private FrameworkElement PreferencesPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("OPTIONS", "SectionLabel"));
        var sample = GenDate.Parse("5 Mar 1890").Display;
        var lines = new[]
        {
            $"Dates shown as {sample}, typed {(GenDate.DayFirst ? "day first" : "month first")}",
            Person.SurnamesInCapitals ? "Surnames in capitals" : "Surnames as typed",
            $"Starts in {_settings!.StartWorkspace}",
            _settings.ShowDataWarnings ? "Unlikely dates are flagged as you type" : "Unlikely-date alerts are off",
        };
        foreach (var line in lines)
        {
            var t = UiKit.Text(line, size: 12.5);
            t.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(t);
        }
        var open = UiKit.Button("SecondaryButton", "Options...", (_, _) => { _window!.OpenOptions(); Build(); }, "Tools");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(open);
        return sp;
    }
}
