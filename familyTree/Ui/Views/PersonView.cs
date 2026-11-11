using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>
/// The People workspace's Person view, opened by double-clicking someone on the tree: every fact
/// in one table grouped as personal information, individual facts and facts shared with each
/// spouse; the selected fact's details and sources beside it; notes below; and links to the
/// spouse and parents across the top.
/// </summary>
public sealed class PersonView : UserControl
{
    private const string NameRow = "name";
    private const string SexRow = "sex";

    private readonly AppState _state;
    private readonly StackPanel _nav = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _rows = new();
    private readonly ContentControl _detail = new();
    private readonly TextBox _notes = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
        Padding = new Thickness(10, 8, 10, 8),
    };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Person? _person;

    /// <summary>The selected row: <see cref="NameRow"/>, <see cref="SexRow"/> or a fact id.  Kept by id, as undo replaces facts.</summary>
    private string _selectedRow = NameRow;

    private object _notesToken = new();

    public PersonView(AppState state)
    {
        _state = state;
        _refresh.Tick += (_, _) => { _refresh.Stop(); BuildRows(); BuildTimeline(); };

        var root = new DockPanel();

        var navBar = new Border { Style = UiKit.Res<Style>("Panel"), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12), Child = _nav };
        DockPanel.SetDock(navBar, Dock.Top);
        root.Children.Add(navBar);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 480 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400), MinWidth = 320 });

        // Facts table above, notes below, with a splitter between.
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 200 });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220), MinHeight = 120 });
        left.Children.Add(new Border { Style = UiKit.Res<Style>("Panel"), Child = BuildFactsPanel() });
        var splitter = new GridSplitter { Height = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(splitter, 1);
        left.Children.Add(splitter);
        var notes = new Border { Style = UiKit.Res<Style>("Panel"), Child = BuildNotesPanel() };
        Grid.SetRow(notes, 2);
        left.Children.Add(notes);
        grid.Children.Add(left);

        var right = new Border { Style = UiKit.Res<Style>("Panel"), Child = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        root.Children.Add(grid);
        Content = root;

        state.EditStateChanged += () => { if (IsVisible) { _refresh.Stop(); _refresh.Start(); } };
    }

    private TreeDocument Doc => _state.Doc;

    /// <summary>Shows a person; the Name row is selected when the person changes.</summary>
    public void Load(Person? person)
    {
        if (person?.Id != _person?.Id) _selectedRow = NameRow;
        _person = person;
        BuildNav();
        BuildRows();
        BuildTimeline();
        BuildDetail();
        _notesToken = new object();
        _notes.Text = person?.Notes ?? "";
        _notes.IsEnabled = person is not null;
    }

    // ---------------------------------------------------------------- navigation strip

    private void BuildNav()
    {
        _nav.Children.Clear();
        var p = _person;
        if (p is null) return;

        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 0) };
        who.Children.Add(UiKit.Avatar(Doc, p, 40));
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(UiKit.Text(p.DisplayName, "H2"));
        names.Children.Add(UiKit.Text(p.LifeSpan.Length > 0 ? p.LifeSpan : "Dates unknown", "Caption"));
        who.Children.Add(names);
        _nav.Children.Add(who);

        void Group(string label, IEnumerable<(Person Person, string Role)> people, Action? add, string addText)
        {
            var g = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(UiKit.Text(label, "SectionLabel"));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (person, role) in people) row.Children.Add(Chip(person, role));
            if (add is not null)
            {
                var b = UiKit.Button("GhostButton", addText, (_, _) => add(), "Plus");
                b.Padding = new Thickness(6, 3, 6, 3);
                row.Children.Add(b);
            }
            g.Children.Add(row);
            _nav.Children.Add(g);
        }

        var spouses = Doc.Spouses(p).Select(s => (s, "Spouse")).ToList();
        Group("SPOUSE", spouses, spouses.Count == 0 ? () => PersonActions.AddSpouse(_state, p) : null, "Add spouse");
        var parents = new List<(Person, string)>();
        if (Doc.Father(p) is { } f) parents.Add((f, "Father"));
        if (Doc.Mother(p) is { } m) parents.Add((m, "Mother"));
        Group("PARENTS", parents, parents.Count < 2 ? () => PersonActions.AddParent(_state, p, Doc.Father(p) is null ? Sex.Male : Sex.Female) : null,
            Doc.Father(p) is null ? "Add father" : "Add mother");
    }

    /// <summary>A small person button: click to show that person here.</summary>
    private Button Chip(Person p, string role)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(UiKit.Avatar(Doc, p, 22));
        content.Children.Add(new TextBlock { Text = p.DisplayName, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Style = UiKit.Res<Style>("SecondaryButton"),
            Content = content,
            Padding = new Thickness(6, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = $"{role}: show {p.DisplayName}",
        };
        b.Click += (_, _) => _state.Select(p, focus: true);
        return b;
    }

    // ---------------------------------------------------------------- facts table

    private const double FactCol = 170, DateCol = 130, SourcesCol = 70, NotesCol = 60, PreferredCol = 96;

    private FrameworkElement BuildFactsPanel()
    {
        var dock = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(16, 12, 12, 8) };
        var add = UiKit.Button("GhostButton", "Add fact", (s, _) =>
        {
            if (_person is null) return;
            FactForm.ShowAddMenu(Doc, (Button)s, _person.Facts, FactScope.Person, _person, f => { _selectedRow = f.Id; Load(_person); });
        }, "Plus");
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);

        // Facts or Timeline.
        var switcher = new Border
        {
            Background = UiKit.Brush("SurfaceAlt"),
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        var factsTab = new RadioButton { Style = UiKit.Res<Style>("Segment"), GroupName = "personViewMode", Content = "Facts", IsChecked = true };
        var timelineTab = new RadioButton { Style = UiKit.Res<Style>("Segment"), GroupName = "personViewMode", Content = "Timeline", ToolTip = "The life in date order, with family and historical events" };
        seg.Children.Add(factsTab);
        seg.Children.Add(timelineTab);
        switcher.Child = seg;
        DockPanel.SetDock(switcher, Dock.Left);
        head.Children.Add(switcher);
        _title = UiKit.Text("Individual and shared facts", "H3");
        _title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_title);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        // Timeline options, shown only with the timeline.
        _timelineOptions.Margin = new Thickness(16, 0, 12, 8);
        _timelineOptions.Visibility = Visibility.Collapsed;
        foreach (var cb in new[] { _withFamily, _withHistory })
        {
            cb.Margin = new Thickness(0, 0, 20, 0);
            cb.Click += (_, _) => BuildTimeline();
            _timelineOptions.Children.Add(cb);
        }
        var manage = UiKit.Button("GhostButton", "Historical events...", (_, _) => { Dialogs.HistoricalEvents(_state); BuildTimeline(); });
        manage.Padding = new Thickness(6, 2, 6, 2);
        _timelineOptions.Children.Add(manage);
        DockPanel.SetDock(_timelineOptions, Dock.Top);
        dock.Children.Add(_timelineOptions);

        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        BuildHeader();
        var host = new Grid();
        var factsScroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 8) };
        var timelineScroll = new ScrollViewer { Content = _timeline, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        host.Children.Add(factsScroll);
        host.Children.Add(timelineScroll);
        dock.Children.Add(host);

        void Switch()
        {
            _showTimeline = timelineTab.IsChecked == true;
            factsScroll.Visibility = _header.Visibility = add.Visibility = _showTimeline ? Visibility.Collapsed : Visibility.Visible;
            timelineScroll.Visibility = _timelineOptions.Visibility = _showTimeline ? Visibility.Visible : Visibility.Collapsed;
            _title.Text = _showTimeline ? "Timeline" : "Individual and shared facts";
            if (_showTimeline) BuildTimeline();
        }
        factsTab.Checked += (_, _) => Switch();
        timelineTab.Checked += (_, _) => Switch();
        return dock;
    }

    // ---------------------------------------------------------------- timeline

    private readonly StackPanel _timeline = new();
    private readonly StackPanel _timelineOptions = new() { Orientation = Orientation.Horizontal };
    private readonly CheckBox _withFamily = new() { Content = "Family events", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _withHistory = new() { Content = "Historical events", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock _title = new();
    private bool _showTimeline;

    private void BuildTimeline()
    {
        _timeline.Children.Clear();
        var p = _person;
        if (p is null || !_showTimeline) return;
        var entries = Timeline.Build(Doc, p, _withFamily.IsChecked == true, _withHistory.IsChecked == true);
        if (entries.Count == 0)
        {
            _timeline.Children.Add(EmptyLine("Nothing dated yet.  Add a birth date and other dated facts to see a timeline."));
            return;
        }
        if (_withHistory.IsChecked == true && Doc.Data.HistoricalEvents.Count == 0)
            _timeline.Children.Add(EmptyLine("This tree has no historical events yet.  Use Historical events... to add the standard list or your own."));
        foreach (var e in entries)
        {
            var g = new Grid { Margin = new Thickness(16, 0, 12, 0), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DateCol) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = UiKit.Brush(e.Kind switch { TimelineKind.Personal => "Brand", TimelineKind.Family => "BrandMid", _ => "Warning" }),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 13, 0, 0),
            };
            g.Children.Add(dot);
            var date = UiKit.Text(e.Date.Display, size: 12.5);
            date.Margin = new Thickness(0, 8, 8, 8);
            date.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(date, 1);
            g.Children.Add(date);
            var what = new StackPanel { Margin = new Thickness(0, 8, 8, 8) };
            what.Children.Add(UiKit.Text(e.Title, weight: e.Kind == TimelineKind.Personal ? FontWeights.SemiBold : FontWeights.Normal));
            if (e.Detail.Length > 0)
            {
                var d = UiKit.Text(e.Detail, "Caption");
                d.TextWrapping = TextWrapping.Wrap;
                d.TextTrimming = TextTrimming.None;
                what.Children.Add(d);
            }
            Grid.SetColumn(what, 2);
            g.Children.Add(what);
            var age = UiKit.Text(e.Age is int a ? $"age {a}" : "", "Caption");
            age.Margin = new Thickness(0, 9, 0, 8);
            age.VerticalAlignment = VerticalAlignment.Top;
            age.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(age, 3);
            g.Children.Add(age);
            var row = new Border { Child = g, BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
            if (e.Person is { } other && e.Kind == TimelineKind.Family)
            {
                row.Cursor = Cursors.Hand;
                row.ToolTip = $"Show {other.DisplayName}";
                row.MouseLeftButtonUp += (_, _) => _state.Select(other, focus: true);
            }
            _timeline.Children.Add(row);
        }
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        var p = _person;
        if (p is null) return;

        // Grouping as Design considerations, Person view: names, sex and
        // identifiers under Personal information, other personal facts under Individual facts.
        _rows.Children.Add(GroupHeading("Personal information"));
        _rows.Children.Add(Row(NameRow, "Name", "", p.DisplayName, "", "", "Preferred"));
        _rows.Children.Add(Row(SexRow, "Sex", "", p.Sex.ToString(), "", "", "Preferred"));
        foreach (var f in Sort(p.Facts.Where(f => FactOrder.PersonalInformationTypes.Contains(f.TypeId))))
            _rows.Children.Add(FactRow(f, p.Facts));

        _rows.Children.Add(GroupHeading("Individual facts"));
        var own = Sort(p.Facts.Where(f => !FactOrder.PersonalInformationTypes.Contains(f.TypeId))).ToList();
        if (own.Count == 0) _rows.Children.Add(EmptyLine("No facts yet.  Use Add fact."));
        foreach (var f in own) _rows.Children.Add(FactRow(f, p.Facts));

        // A spouse order set by hand (Set spouse order) wins; otherwise by marriage date (Design considerations, Person view).
        foreach (var fam in p.SpouseOrder.Count > 0 ? Doc.SpouseFamilies(p) : FactOrder.ByMarriage(Doc.SpouseFamilies(p)))
        {
            var spouse = Doc.GetPerson(fam.OtherPartner(p.Id));
            var heading = GroupHeading(spouse is null ? "Shared facts" : $"Shared facts with {spouse.DisplayName}");
            var addShared = UiKit.Button("GhostButton", "Add", (s, _) =>
                FactForm.ShowAddMenu(Doc, (Button)s, fam.Facts, FactScope.Couple, null, f => { _selectedRow = f.Id; Load(_person); }), "Plus");
            addShared.Padding = new Thickness(6, 2, 6, 2);
            addShared.FontSize = 12;
            DockPanel.SetDock(addShared, Dock.Right);
            heading.Children.Insert(0, addShared);
            _rows.Children.Add(heading);
            var shared = Sort(fam.Facts).ToList();
            if (shared.Count == 0) _rows.Children.Add(EmptyLine("No shared facts."));
            foreach (var f in shared) _rows.Children.Add(FactRow(f, fam.Facts));
        }
    }

    // ---- column sorting: Date (chronological) by default; clicking a heading sorts by it, again to reverse ----

    private int _sortColumn = 1;
    private bool _sortDescending;
    private readonly ContentControl _header = new();

    private IEnumerable<Fact> Sort(IEnumerable<Fact> facts)
    {
        var chronological = FactOrder.Sorted(facts).ToList();
        var text = StringComparer.CurrentCultureIgnoreCase;
        IEnumerable<Fact> sorted = _sortColumn switch
        {
            0 => chronological.OrderBy(f => f.Type.Name, text),
            2 => chronological.OrderBy(f => FactForm.Detail(Doc, f), text),
            3 => chronological.OrderBy(f => f.Citations.Count),
            _ => chronological,
        };
        return _sortDescending ? sorted.Reverse() : sorted;
    }

    private void BuildHeader()
    {
        var arrow = _sortDescending ? " ▼" : " ▲";
        string Label(string text, int col) => _sortColumn == col ? text + arrow : text;
        _header.Content = Row(null, Label("Fact", 0), Label("Date", 1), Label("Place / Description", 2), Label("Sources", 3), "Notes", "Preferred", header: true);
    }

    private void SortBy(int column)
    {
        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        BuildHeader();
        BuildRows();
    }

    private static DockPanel GroupHeading(string text)
    {
        var d = new DockPanel { Margin = new Thickness(16, 12, 12, 2) };
        var t = UiKit.Text(text, size: 12.5, brush: UiKit.Brush("Brand"), weight: FontWeights.SemiBold);
        t.VerticalAlignment = VerticalAlignment.Center;
        d.Children.Add(t);
        return d;
    }

    private static FrameworkElement EmptyLine(string text)
    {
        var t = UiKit.Text(text, "Caption");
        t.Margin = new Thickness(26, 4, 12, 4);
        return t;
    }

    private FrameworkElement FactRow(Fact f, List<Fact> facts)
    {
        // "Preferred" only on the preferred fact of its type; private facts carry a lock (Design considerations, Person view).
        var preferredOfType = facts.FirstOrDefault(x => x.TypeId == f.TypeId && x.Preferred) ?? facts.FirstOrDefault(x => x.TypeId == f.TypeId);
        var preferred = preferredOfType == f ? "Preferred" : "";
        var date = f.ParsedDate;
        return Row(f.Id, f.Type.Name, date.Display, FactForm.Detail(Doc, f), f.Citations.Count.ToString(), f.Note.Length > 0 ? "1" : "0", preferred,
            dateWarning: !date.IsValid && !date.IsEmpty, isPrivate: f.Private);
    }

    /// <summary>One table row; <paramref name="key"/> is what selecting it selects (null for the header).</summary>
    private FrameworkElement Row(string? key, string fact, string date, string detail, string sources, string notes, string preferred, bool header = false, bool dateWarning = false, bool isPrivate = false)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FactCol) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DateCol) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SourcesCol) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NotesCol) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreferredCol) });

        void Cell(string text, int col, Brush? brush = null, bool centre = false)
        {
            var t = header
                ? UiKit.Text(text, "Caption", weight: FontWeights.SemiBold)
                : UiKit.Text(text, size: 13, brush: brush);
            if (centre) t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(col == 0 ? 26 : 4, 6, 6, 6);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
            if (header && col <= 3)
            {
                t.Cursor = Cursors.Hand;
                t.Background = Brushes.Transparent;
                t.ToolTip = "Sort by this column; click again to reverse";
                t.MouseLeftButtonUp += (_, _) => SortBy(col);
            }
        }

        var muted = UiKit.Brush("Muted");
        Cell(fact, 0, header ? null : UiKit.Brush("Ink"));
        Cell(date, 1, dateWarning ? UiKit.Brush("Warning") : null);
        Cell(detail, 2);
        Cell(sources, 3, sources == "0" ? muted : UiKit.Brush("Brand"), centre: true);
        Cell(notes, 4, notes == "0" ? muted : UiKit.Brush("Brand"), centre: true);
        Cell(preferred, 5, muted);
        if (isPrivate)
        {
            var lockIcon = UiKit.Icon("Lock", 13, UiKit.Brush("Warning"));
            lockIcon.HorizontalAlignment = HorizontalAlignment.Left;
            lockIcon.Margin = new Thickness(8, 0, 0, 0);
            lockIcon.ToolTip = "Private";
            g.Children.Add(lockIcon);
        }
        if (header)
        {
            g.Background = UiKit.Brush("SurfaceAlt");
            return new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 1), Child = g };
        }

        var selected = key == _selectedRow;
        var border = new Border
        {
            Child = g,
            Margin = new Thickness(8, 1, 8, 1),
            CornerRadius = new CornerRadius(6),
            Background = selected ? UiKit.Brush("BrandSoft") : Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        g.Margin = new Thickness(-8, 0, 0, 0);
        border.MouseEnter += (_, _) => { if (key != _selectedRow) border.Background = UiKit.Brush("Hover"); };
        border.MouseLeave += (_, _) => { if (key != _selectedRow) border.Background = Brushes.Transparent; };
        border.MouseLeftButtonUp += (_, _) =>
        {
            _selectedRow = key!;
            BuildRows();
            BuildDetail();
        };
        return border;
    }

    // ---------------------------------------------------------------- detail panel

    private void BuildDetail()
    {
        var p = _person;
        if (p is null)
        {
            _detail.Content = null;
            return;
        }
        var sp = new StackPanel { Margin = new Thickness(18, 16, 18, 20) };

        if (_selectedRow == NameRow)
        {
            sp.Children.Add(UiKit.Text("Name", "H2"));
            sp.Children.Add(Gap(UiKit.Field(Doc, "Given names", () => p.Given, v => p.Given = v)));
            sp.Children.Add(UiKit.Field(Doc, "Surname", () => p.Surname, v => p.Surname = v));
            sp.Children.Add(UiKit.Field(Doc, "Suffix", () => p.Suffix, v => p.Suffix = v));
            var aka = UiKit.Text("Other names are kept as \"Also Known As\" facts.", "Caption");
            aka.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(aka);
        }
        else if (_selectedRow == SexRow)
        {
            sp.Children.Add(UiKit.Text("Sex", "H2"));
            var box = new ComboBox { ItemsSource = Enum.GetValues<Sex>(), SelectedItem = p.Sex, Margin = new Thickness(0, 12, 0, 0) };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is Sex s && s != p.Sex) Doc.Change(() => p.Sex = s, structural: false);
                BuildRows();
            };
            sp.Children.Add(box);
        }
        else if (FindFact(_selectedRow) is { } found)
        {
            var (fact, facts, owner) = found;
            var title = owner is Family fam ? $"{fact.Type.Name} of {Doc.CoupleName(fam)}" : fact.Type.Name;
            var heading = UiKit.Text(title, "H2");
            heading.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(heading);
            var form = FactForm.Build(Doc, fact, facts, owner as Person, () => { _refresh.Stop(); _refresh.Start(); },
                () => Load(_person), () => { _selectedRow = NameRow; Load(_person); });
            form.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(form);
        }
        else
        {
            _selectedRow = NameRow;
            BuildDetail();
            return;
        }
        _detail.Content = sp;
    }

    private static FrameworkElement Gap(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 12, 0, 12);
        return e;
    }

    private (Fact Fact, List<Fact> Facts, object Owner)? FindFact(string id)
    {
        if (_person is null) return null;
        if (_person.Facts.FirstOrDefault(f => f.Id == id) is { } own) return (own, _person.Facts, _person);
        foreach (var fam in Doc.SpouseFamilies(_person))
            if (fam.Facts.FirstOrDefault(f => f.Id == id) is { } shared) return (shared, fam.Facts, fam);
        return null;
    }

    // ---------------------------------------------------------------- notes

    private FrameworkElement BuildNotesPanel()
    {
        var dock = new DockPanel { Margin = new Thickness(16, 12, 16, 14) };
        var label = UiKit.Text("Notes", "H3");
        label.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(label, Dock.Top);
        dock.Children.Add(label);
        _notes.GotKeyboardFocus += (_, _) => _notesToken = new object();
        _notes.TextChanged += (_, _) =>
        {
            if (_person is null || _notes.Text == _person.Notes) return;
            Doc.BeginEdit(_notesToken);
            _person.Notes = _notes.Text;
            Doc.MarkEdited();
        };
        _notes.LostKeyboardFocus += (_, _) => Doc.EndEdit();
        dock.Children.Add(_notes);
        return dock;
    }
}
