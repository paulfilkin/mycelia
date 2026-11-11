using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

public partial class PeopleView : UserControl, IWorkspace
{
    private enum TreeMode { Pedigree, Family, Descendants }

    private AppState? _state;
    private PersonEditor? _editor;
    private PersonView? _personView;
    private TreeMode _mode = TreeMode.Pedigree;
    private bool _syncing;
    private readonly DispatcherTimer _redraw;
    private List<Person> _all = [];

    public PeopleView()
    {
        InitializeComponent();
        _redraw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _redraw.Tick += (_, _) => { _redraw.Stop(); DrawTree(); };
    }

    private TreeDocument Doc => _state!.Doc;

    public void Attach(AppState state)
    {
        _state = state;
        _editor = new PersonEditor(state);
        EditorHost.Content = _editor;
        _personView = new PersonView(state);
        PersonHost.Content = _personView;
        state.DocumentReplaced += () => { RefreshIndex(); DrawTree(); };
        state.TreeChanged += () => { RefreshIndex(); DrawTree(); };
        state.SelectionChanged += OnSelectionChanged;
        state.BookmarksChanged += () => { if (ListBookmarks.IsChecked == true) RefreshIndex(); };
        state.EditStateChanged += () => { if (IsVisible) _redraw.Start(); };
        // The index's right-click menu, filled for the person when it opens (Shift+F10 and the Menu key too).
        IndexList.ContextMenu = new ContextMenu();
        IndexList.ContextMenuOpening += OnIndexMenu;
        RefreshIndex();
        OnSelectionChanged();
    }

    public void OnShown() => DrawTree();

    // ---------------------------------------------------------------- index

    private void RefreshIndex()
    {
        if (_state is null) return;
        // The History list is the people viewed, each once, in the index's own sort order (Design considerations, History).
        IEnumerable<Person> people = ListHistory.IsChecked == true
            ? Doc.Data.History.Distinct().Select(Doc.GetPerson).OfType<Person>()
            : ListBookmarks.IsChecked == true
                ? Doc.Data.Bookmarks.Distinct().Select(Doc.GetPerson).OfType<Person>()
                : Doc.Data.People;
        _all = IndexSort.Sort(Doc, people, (IndexSortOrder)Math.Max(0, SortBox.SelectedIndex));
        ApplyFilter();
    }

    private void Remember(Person? p)
    {
        if (p is null || _state is null) return;
        var history = Doc.Data.History;
        if (history.Count > 0 && history[^1] == p.Id) return;
        // Oldest entries are dropped once the limit is reached (Design considerations, History).
        while (history.Count >= IndexSort.HistoryLimit) history.RemoveAt(0);
        history.Add(p.Id);
    }

    private void OnListChanged(object sender, RoutedEventArgs e) => RefreshIndex();

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) => RefreshIndex();

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var items = q.Length == 0 ? _all : _all.Where(p => Dialogs.Matches(p, q) || BirthPlaceMatches(p, q)).ToList();
        _syncing = true;
        IndexList.ItemsSource = items;
        IndexList.SelectedItem = _state?.Selected;
        _syncing = false;
        var noun = ListHistory.IsChecked == true ? "viewed" : ListBookmarks.IsChecked == true ? "bookmarked" : "people";
        IndexCount.Text = q.Length == 0 ? $"{_all.Count} {noun}" : $"{items.Count} of {_all.Count}";
    }

    private bool BirthPlaceMatches(Person p, string q) =>
        Doc.PlaceName(p.Birth?.PlaceId).Contains(q, StringComparison.CurrentCultureIgnoreCase);

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnIndexSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || IndexList.SelectedItem is not Person p) return;
        _state!.Select(p, focus: true);
    }

    private void OnIndexDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IndexList.SelectedItem is Person p) OpenPersonView(p);
    }

    /// <summary>Right-click on a name in the index (a right-click selects it first), or the keyboard on the selected name.</summary>
    private void OnIndexMenu(object sender, ContextMenuEventArgs e)
    {
        var byMouse = e.CursorLeft >= 0 || e.CursorTop >= 0;
        var onItem = ItemsControl.ContainerFromElement(IndexList, (DependencyObject)e.OriginalSource) is ListBoxItem;
        if (IndexList.SelectedItem is not Person p || (byMouse && !onItem))
        {
            e.Handled = true;
            return;
        }
        PersonMenu.Fill(IndexList.ContextMenu, _state!, p, OpenPersonView);
    }

    private void OnSelectionChanged()
    {
        if (_state is null) return;
        Remember(_state.Selected);
        if (ListHistory.IsChecked == true) RefreshIndex();
        _syncing = true;
        IndexList.SelectedItem = _state.Selected;
        if (_state.Selected is not null) IndexList.ScrollIntoView(_state.Selected);
        _syncing = false;
        _editor!.Load(_state.Selected);
        if (ViewPerson.IsChecked == true) _personView!.Load(_state.Selected);
        DrawTree();
    }

    private void OnAddUnrelated(object sender, RoutedEventArgs e) => PersonActions.AddUnrelated(_state!);

    // ---------------------------------------------------------------- Tree and Person views

    /// <summary>Open a person's Person view (double-click in the index, the right-click menu), centring the tree on them.</summary>
    private void OpenPersonView(Person p)
    {
        _state!.Select(p, focus: true);
        ViewPerson.IsChecked = true;
    }

    public void ShowPersonView() => ViewPerson.IsChecked = true;

    /// <summary>Whether the Person view is on show rather than the Tree view.</summary>
    public bool IsPersonViewShown => ViewPerson.IsChecked == true;

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (_state is null) return;
        var person = ViewPerson.IsChecked == true;
        PersonHost.Visibility = person ? Visibility.Visible : Visibility.Collapsed;
        TreeLayout.Visibility = person ? Visibility.Collapsed : Visibility.Visible;
        ViewHint.Text = person
            ? "Choose Tree to go back; the spouse and parent buttons move between relatives"
            : "Double-click anyone on the tree to centre it on them; F3 opens their Person view";
        if (person) _personView!.Load(_state.Selected);
        else DrawTree();
    }

    // ---------------------------------------------------------------- tree toolbar

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        _mode = sender == ModeFamily ? TreeMode.Family : sender == ModeDescendants ? TreeMode.Descendants : TreeMode.Pedigree;
        if (_state is not null) DrawTree();
    }

    private void OnGoHome(object sender, RoutedEventArgs e) => _state!.Select(Doc.HomePerson, focus: true);

    /// <summary>Until the person zooms by hand, the tree is scaled to fit its panel.</summary>
    private bool _autoFit = true;

    private void SetZoom(double z, bool manual = true)
    {
        if (manual) _autoFit = false;
        z = Math.Clamp(z, 0.5, 2.0);
        TreeScale.ScaleX = TreeScale.ScaleY = z;
        ZoomText.Text = $"{Math.Round(z * 100)}%";
        ZoomText.ToolTip = _autoFit ? "Fitted to the panel" : "Click to fit the tree to the panel";
    }

    private void FitToPanel()
    {
        if (!_autoFit || TreeScroll.ViewportWidth <= 0 || TreeCanvas.Width is not > 0) return;
        var fit = Math.Min((TreeScroll.ViewportWidth - 8) / TreeCanvas.Width, (TreeScroll.ViewportHeight - 8) / TreeCanvas.Height);
        SetZoom(Math.Clamp(fit, 0.6, 1.0), manual: false);
    }

    private void OnTreeSizeChanged(object sender, SizeChangedEventArgs e) => FitToPanel();

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(TreeScale.ScaleX + 0.1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(TreeScale.ScaleX - 0.1);

    private void OnZoomTextClick(object sender, MouseButtonEventArgs e)
    {
        _autoFit = true;
        FitToPanel();
    }

    private void OnTreeWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(TreeScale.ScaleX + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    // ---------------------------------------------------------------- drawing

    private void DrawTree()
    {
        if (_state is null) return;
        TreeCanvas.Children.Clear();
        FamilyStrip.Child = null;
        var focus = _state.Focus ?? Doc.HomePerson;
        if (focus is null)
        {
            TreeCanvas.Width = 600;
            TreeCanvas.Height = 400;
            var empty = UiKit.EmptyState("Tree", "Your tree is empty", "Add the first person to start. It is usual to begin with yourself and work backwards.");
            var add = UiKit.Button("PrimaryButton", "Add first person", (_, _) => PersonActions.AddUnrelated(_state), "Plus");
            add.HorizontalAlignment = HorizontalAlignment.Center;
            add.Margin = new Thickness(0, 14, 0, 0);
            ((StackPanel)empty).Children.Add(add);
            Canvas.SetLeft(empty, 120);
            Canvas.SetTop(empty, 80);
            TreeCanvas.Children.Add(empty);
            FamilyStrip.Visibility = Visibility.Collapsed;
            return;
        }

        switch (_mode)
        {
            case TreeMode.Pedigree:
                DrawPedigree(focus);
                FamilyStrip.Visibility = Visibility.Visible;
                FamilyStrip.Child = BuildFamilyStrip(focus);
                break;
            case TreeMode.Family:
                DrawFamily(focus);
                FamilyStrip.Visibility = Visibility.Collapsed;
                break;
            case TreeMode.Descendants:
                DrawDescendants(focus);
                FamilyStrip.Visibility = Visibility.Collapsed;
                break;
        }
        Dispatcher.BeginInvoke(new Action(FitToPanel), DispatcherPriority.Loaded);
    }

    private static readonly (double W, double H)[] PedigreeSizes = [(220, 78), (206, 66), (196, 58), (188, 50)];
    // The first column leaves room on its left for the button that moves the tree down to a child.
    private static readonly double[] PedigreeX = [50, 300, 536, 762];

    private void DrawPedigree(Person focus)
    {
        const double slot = 64;
        const double top = 24;
        var total = slot * 8;
        TreeCanvas.Width = PedigreeX[3] + PedigreeSizes[3].W + 48;
        TreeCanvas.Height = total + top * 2;

        var people = new Person?[4][];
        for (var g = 0; g < 4; g++) people[g] = new Person?[1 << g];
        people[0][0] = focus;
        for (var g = 0; g < 3; g++)
        {
            for (var i = 0; i < people[g].Length; i++)
            {
                var p = people[g][i];
                if (p is null) continue;
                people[g + 1][2 * i] = Doc.Father(p);
                people[g + 1][2 * i + 1] = Doc.Mother(p);
            }
        }

        double CenterY(int g, int i) => top + (i + 0.5) * total / (1 << g);

        // Connectors first, so cards sit on top.
        for (var g = 0; g < 3; g++)
        {
            for (var i = 0; i < people[g].Length; i++)
            {
                var child = people[g][i];
                if (child is null) continue;
                var x1 = PedigreeX[g] + PedigreeSizes[g].W;
                var y1 = CenterY(g, i);
                var x2 = PedigreeX[g + 1];
                var xm = (x1 + x2) / 2;
                foreach (var k in new[] { 2 * i, 2 * i + 1 })
                {
                    var y2 = CenterY(g + 1, k);
                    var dashed = people[g + 1][k] is null;
                    if (dashed && g + 1 > 2) continue;
                    AddConnector(new[] { new Point(x1, y1), new Point(xm, y1), new Point(xm, y2), new Point(x2, y2) }, dashed);
                }
            }
        }

        for (var g = 0; g < 4; g++)
        {
            var (w, h) = PedigreeSizes[g];
            for (var i = 0; i < people[g].Length; i++)
            {
                var p = people[g][i];
                var y = CenterY(g, i) - h / 2;
                if (p is not null)
                {
                    var hasMore = g == 3 && Doc.Parents(p).Any();
                    Place(PersonCard(p, w, h, g == 0, hasMore), PedigreeX[g], y);
                }
                else if (g > 0 && people[g - 1][i / 2] is { } child && g <= 2)
                {
                    var sex = i % 2 == 0 ? Sex.Male : Sex.Female;
                    var label = sex == Sex.Male ? "Add father" : "Add mother";
                    Place(EmptySlot(label, w, h, () => PersonActions.AddParent(_state!, child, sex)), PedigreeX[g], y);
                }
            }
        }

        // Down a generation: a button on the focus person's left when they have children.
        var kids = Doc.Children(focus).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
        if (kids.Count > 0)
        {
            var tip = kids.Count == 1 ? $"Show {kids[0].DisplayName}" : $"Show one of {focus.DisplayName}'s {kids.Count} children";
            Button? down = null;
            down = NavButton("ArrowLeft", tip, () => ChooseChild(down!, kids));
            down.BorderBrush = UiKit.Brush("BorderStrong");
            var cy = CenterY(0, 0);
            AddConnector([new Point(14 + 26, cy), new Point(PedigreeX[0], cy)], false);
            Place(down, 14, cy - 13);
        }

        // The spouse sits directly under the focus person.
        var fam = Doc.SpouseFamilies(focus).FirstOrDefault();
        var spouse = fam is null ? null : Doc.GetPerson(fam.OtherPartner(focus.Id));
        if (spouse is not null)
        {
            var (w0, h0) = PedigreeSizes[0];
            var y0 = CenterY(0, 0) + h0 / 2 + 8;
            Place(PersonCard(spouse, w0 - 18, 56, false, false, Doc.Marriage(fam!)), PedigreeX[0] + 18, y0);
            AddConnector([new Point(PedigreeX[0] + 9, CenterY(0, 0) + h0 / 2), new Point(PedigreeX[0] + 9, y0 + 28), new Point(PedigreeX[0] + 18, y0 + 28)], false);
        }
    }

    private int _familyIndex;
    private string? _familyFocusId;

    /// <summary>
    /// The family under the pedigree: the couple side by side (each with their own parents above),
    /// the marriage, and a table of children.
    /// </summary>
    private FrameworkElement BuildFamilyStrip(Person focus)
    {
        if (_familyFocusId != focus.Id) { _familyFocusId = focus.Id; _familyIndex = 0; }
        var families = Doc.SpouseFamilies(focus).ToList();
        if (_familyIndex >= families.Count) _familyIndex = 0;
        var fam = families.Count > 0 ? families[_familyIndex] : null;
        var spouse = fam is null ? null : Doc.GetPerson(fam.OtherPartner(focus.Id));

        var root = new StackPanel();
        var couple = new Grid();
        couple.ColumnDefinitions.Add(new ColumnDefinition());
        couple.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        couple.ColumnDefinitions.Add(new ColumnDefinition());
        couple.Children.Add(PartnerPanel(focus, isFocus: true));
        FrameworkElement right = spouse is not null
            ? PartnerPanel(spouse, isFocus: false)
            : EmptySlot("Add spouse", double.NaN, 96, () => PersonActions.AddSpouse(_state!, focus));
        if (spouse is null) right.Margin = new Thickness(0, 30, 0, 0);
        Grid.SetColumn(right, 2);
        couple.Children.Add(right);
        root.Children.Add(couple);

        // Marriage line
        var marriageRow = new DockPanel { Margin = new Thickness(2, 10, 0, 6) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (families.Count > 1)
        {
            tools.Children.Add(UiKit.Button("GhostButton", $"Spouse {_familyIndex + 1} of {families.Count}", (_, _) =>
            {
                _familyIndex = (_familyIndex + 1) % families.Count;
                FamilyStrip.Child = BuildFamilyStrip(focus);
            }, "ArrowRight", "Show the next spouse"));
        }
        if (spouse is not null)
            tools.Children.Add(UiKit.Button("GhostButton", "Add spouse", (_, _) => PersonActions.AddSpouse(_state!, focus), "Plus"));
        DockPanel.SetDock(tools, Dock.Right);
        marriageRow.Children.Add(tools);
        var rings = UiKit.Icon("Rings", 16, UiKit.Brush("Brand"));
        rings.Margin = new Thickness(0, 0, 8, 0);
        marriageRow.Children.Add(rings);
        var m = fam?.Marriage;
        var marriageText = fam is null ? "No spouse or partner recorded"
            : m is null ? $"{fam.Type}, no marriage recorded"
            : "Marriage: " + string.Join(" in ", new[] { m.ParsedDate.Display, Doc.PlaceName(m.PlaceId) }.Where(s => s.Length > 0));
        if (fam is not null && fam.Status is not (CoupleStatus.Ongoing or CoupleStatus.Unknown or CoupleStatus.None)) marriageText += $"  ({fam.Status})";
        var mt = UiKit.Text(marriageText, weight: FontWeights.Medium);
        mt.VerticalAlignment = VerticalAlignment.Center;
        marriageRow.Children.Add(mt);
        root.Children.Add(marriageRow);

        // Children table
        var kids = fam is null ? Doc.Children(focus).ToList() : Doc.Children(fam).ToList();
        var table = new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        var rows = new StackPanel();
        rows.Children.Add(ChildRow(null, header: $"Children: {kids.Count}"));
        foreach (var k in kids) rows.Children.Add(ChildRow(k));
        var add = UiKit.IconText("Plus", "Add child", 13);
        TextElement.SetForeground(add, UiKit.Brush("Brand"));
        var addRow = new Border { Padding = new Thickness(40, 6, 8, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = add };
        addRow.MouseEnter += (_, _) => addRow.Background = UiKit.Brush("BrandSofter");
        addRow.MouseLeave += (_, _) => addRow.Background = Brushes.Transparent;
        addRow.MouseLeftButtonUp += (_, _) => PersonActions.AddChild(_state!, focus, fam);
        rows.Children.Add(addRow);
        table.Child = new ScrollViewer { Content = rows, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(table);
        return root;
    }

    /// <summary>One partner: their parents above, then name, birth and death.</summary>
    private FrameworkElement PartnerPanel(Person p, bool isFocus)
    {
        var sp = new StackPanel();
        var father = Doc.Father(p);
        var mother = Doc.Mother(p);
        var parentsText = (father, mother) switch
        {
            (null, null) => "Add parents",
            _ => string.Join(" & ", new[] { father?.Given, mother?.Given }.Where(s => !string.IsNullOrEmpty(s))),
        };
        var parents = UiKit.Button("SecondaryButton", parentsText, (_, _) =>
        {
            var parent = father ?? mother;
            if (parent is not null) _state!.Select(parent, focus: true);
            else PersonActions.AddParent(_state!, p, Sex.Male);
        }, father is null && mother is null ? "Plus" : "ArrowLeft", father is null && mother is null ? $"Add the parents of {p.Given}" : $"Go to the parents of {p.Given}");
        parents.HorizontalAlignment = HorizontalAlignment.Stretch;
        parents.Padding = new Thickness(8, 3, 8, 3);
        parents.FontSize = 12;
        parents.Margin = new Thickness(0, 0, 0, 6);
        sp.Children.Add(parents);

        var selected = _state!.Selected == p;
        var card = new Border
        {
            Background = UiKit.Brush("Surface"),
            BorderBrush = selected ? UiKit.Brush("Brand") : UiKit.Brush("Border"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = Cursors.Hand,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var strip = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = UiKit.SexBrush(p.Sex), Margin = new Thickness(0, 2, 10, 2) };
        grid.Children.Add(strip);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(p.DisplayName, size: 14, weight: FontWeights.SemiBold));
        text.Children.Add(Detail("Born", p.Birth));
        text.Children.Add(Detail("Died", p.Death));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        card.Child = grid;
        card.ToolTip = isFocus ? "Right-click for more" : "Double-click to centre the tree here";
        card.MouseLeftButtonDown += (_, e) =>
        {
            _state.Select(p, focus: e.ClickCount == 2);
            e.Handled = true;
        };
        card.MouseRightButtonUp += (_, e) => { PersonMenu.Show(_state, p, OpenPersonView); e.Handled = true; };
        sp.Children.Add(card);
        return sp;
    }

    private FrameworkElement Detail(string label, Fact? f)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(UiKit.Text(label, "Caption"));
        var value = f is null ? "" : string.Join(", ", new[] { f.ParsedDate.Display, Doc.PlaceName(f.PlaceId) }.Where(s => s.Length > 0));
        var t = UiKit.Text(value, size: 12.5);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>A row of the children table; with no child, the header row.</summary>
    private FrameworkElement ChildRow(Person? child, string? header = null)
    {
        var g = new Grid { Background = child is null ? UiKit.Brush("SurfaceAlt") : Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        void Cell(UIElement e, int col)
        {
            if (e is FrameworkElement fe) { fe.Margin = new Thickness(col == 0 ? 10 : 4, 5, 6, 5); fe.VerticalAlignment = VerticalAlignment.Center; }
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }
        if (child is null)
        {
            Cell(UiKit.Text("Go", "Caption", weight: FontWeights.SemiBold), 0);
            Cell(UiKit.Text(header ?? "", "Caption", weight: FontWeights.SemiBold), 1);
            Cell(UiKit.Text("Sex", "Caption", weight: FontWeights.SemiBold), 2);
            Cell(UiKit.Text("Birth", "Caption", weight: FontWeights.SemiBold), 3);
            Cell(UiKit.Text("Death", "Caption", weight: FontWeights.SemiBold), 4);
            return g;
        }
        var go = UiKit.Icon("ArrowLeft", 14, UiKit.Brush("Brand"));
        go.ToolTip = $"Centre the tree on {child.DisplayName}";
        Cell(go, 0);
        Cell(UiKit.Text(child.DisplayName, weight: FontWeights.Medium), 1);
        Cell(UiKit.Text(child.Sex switch { Sex.Male => "M", Sex.Female => "F", _ => "?" }, "Caption"), 2);
        string When(Fact? f) => f is null ? "" : string.Join(" in ", new[] { f.ParsedDate.Display, Doc.PlaceName(f.PlaceId) }.Where(s => s.Length > 0));
        Cell(UiKit.Text(When(child.Birth), size: 12.5), 3);
        Cell(UiKit.Text(When(child.Death), size: 12.5), 4);
        var selected = _state!.Selected == child;
        if (selected) g.Background = UiKit.Brush("BrandSoft");
        g.Cursor = Cursors.Hand;
        g.MouseEnter += (_, _) => { if (!selected) g.Background = UiKit.Brush("Hover"); };
        g.MouseLeave += (_, _) => { if (!selected) g.Background = Brushes.Transparent; };
        g.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == go) { _state.Select(child, focus: true); e.Handled = true; return; }
            _state.Select(child, focus: e.ClickCount == 2);
            e.Handled = true;
        };
        g.MouseRightButtonUp += (_, e) => { PersonMenu.Show(_state, child, OpenPersonView); e.Handled = true; };
        return new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 10, 0);
        return e;
    }

    private void DrawFamily(Person focus)
    {
        const double cardW = 236, cardH = 72;
        var fam = Doc.SpouseFamilies(focus).FirstOrDefault();
        var spouse = fam is null ? null : Doc.GetPerson(fam.OtherPartner(focus.Id));
        var children = fam is null ? new List<Person>() : Doc.Children(fam).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
        var father = Doc.Father(focus);
        var mother = Doc.Mother(focus);

        const double childW = 200, childH = 62, gap = 16;
        var perRow = Math.Max(1, Math.Min(4, children.Count + 1));
        var rowWidth = perRow * childW + (perRow - 1) * gap;
        var width = Math.Max(2 * cardW + 80, rowWidth) + 80;
        var cx = width / 2;
        TreeCanvas.Width = width;

        // Parents
        var yParents = 36.0;
        var xFather = cx - cardW - 30;
        var xMother = cx + 30;
        Place(UiKit.Text("PARENTS", "SectionLabel"), xFather, yParents - 22);
        if (father is not null) Place(PersonCard(father, cardW, cardH, false, false), xFather, yParents);
        else Place(EmptySlot("Add father", cardW, cardH, () => PersonActions.AddParent(_state!, focus, Sex.Male)), xFather, yParents);
        if (mother is not null) Place(PersonCard(mother, cardW, cardH, false, false), xMother, yParents);
        else Place(EmptySlot("Add mother", cardW, cardH, () => PersonActions.AddParent(_state!, focus, Sex.Female)), xMother, yParents);
        AddConnector([new Point(xFather + cardW, yParents + cardH / 2), new Point(xMother, yParents + cardH / 2)], father is null || mother is null);

        // Couple
        var yCouple = yParents + cardH + 80;
        AddConnector([new Point(cx, yParents + cardH / 2), new Point(cx, yCouple - 18), new Point(xFather + cardW / 2, yCouple - 18), new Point(xFather + cardW / 2, yCouple)], false);
        Place(UiKit.Text("FAMILY", "SectionLabel"), xFather, yCouple - 40);
        Place(PersonCard(focus, cardW, cardH + 8, true, false), xFather, yCouple);
        if (spouse is not null) Place(PersonCard(spouse, cardW, cardH + 8, false, false), xMother, yCouple);
        else Place(EmptySlot("Add spouse", cardW, cardH + 8, () => PersonActions.AddSpouse(_state!, focus)), xMother, yCouple);
        AddConnector([new Point(xFather + cardW, yCouple + (cardH + 8) / 2), new Point(xMother, yCouple + (cardH + 8) / 2)], spouse is null);

        if (fam is not null)
        {
            var married = fam.Marriage;
            var rings = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var ic = UiKit.Icon("Rings", 18, UiKit.Brush("Brand"));
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            rings.Children.Add(new Border { Background = UiKit.Brush("SurfaceAlt"), Padding = new Thickness(4), Child = ic, HorizontalAlignment = HorizontalAlignment.Center });
            if (married is not null && married.ParsedDate.Display.Length > 0)
                rings.Children.Add(UiKit.Text(married.ParsedDate.Display, "Caption"));
            rings.Width = 58;
            Place(rings, cx - 29, yCouple + (cardH + 8) / 2 - 14);
        }

        // Children
        var yChildren = yCouple + cardH + 8 + 70;
        Place(UiKit.Text(children.Count == 0 ? "CHILDREN" : $"CHILDREN ({children.Count})", "SectionLabel"), cx - rowWidth / 2, yChildren - 26);
        var items = children.Cast<Person?>().Append(null).ToList();
        var rows = (int)Math.Ceiling(items.Count / (double)perRow);
        var midY = yCouple + cardH + 8 + 30;
        AddConnector([new Point(cx, yCouple + (cardH + 8) / 2 + 22), new Point(cx, midY)], fam is null);
        for (var i = 0; i < items.Count; i++)
        {
            var r = i / perRow;
            var c = i % perRow;
            var x = cx - rowWidth / 2 + c * (childW + gap);
            var y = yChildren + r * (childH + 20);
            if (items[i] is { } child)
            {
                if (r == 0) AddConnector([new Point(cx, midY), new Point(x + childW / 2, midY), new Point(x + childW / 2, y)], false);
                Place(PersonCard(child, childW, childH, false, false), x, y);
            }
            else
            {
                Place(EmptySlot("Add child", childW, childH, () => PersonActions.AddChild(_state!, focus, fam)), x, y);
            }
        }
        TreeCanvas.Height = yChildren + rows * (childH + 20) + 30;
    }

    private void DrawDescendants(Person focus)
    {
        const double w = 184, h = 58, gapX = 14, gapY = 64;
        const int depth = 3;

        double Width(Person p, int level)
        {
            if (level >= depth - 1) return w;
            var kids = Doc.Children(p).ToList();
            if (kids.Count == 0) return w;
            return Math.Max(w, kids.Sum(k => Width(k, level + 1)) + gapX * (kids.Count - 1));
        }

        double Layout(Person p, int level, double left)
        {
            var span = Width(p, level);
            var x = left + span / 2 - w / 2;
            var y = 30 + level * (h + gapY);
            if (level < depth - 1)
            {
                var kids = Doc.Children(p).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
                var cursor = left + (span - (kids.Sum(k => Width(k, level + 1)) + gapX * Math.Max(0, kids.Count - 1))) / 2;
                foreach (var k in kids)
                {
                    var kw = Width(k, level + 1);
                    var kx = Layout(k, level + 1, cursor);
                    var ky = 30 + (level + 1) * (h + gapY);
                    var midY = y + h + gapY / 2;
                    AddConnector([new Point(x + w / 2, y + h), new Point(x + w / 2, midY), new Point(kx + w / 2, midY), new Point(kx + w / 2, ky)], false);
                    cursor += kw + gapX;
                }
            }
            var spouse = Doc.Spouses(p).FirstOrDefault();
            Place(PersonCard(p, w, h, p == focus, false, spouse is null ? null : "m. " + spouse.DisplayName), x, y);
            return x;
        }

        var total = Width(focus, 0);
        TreeCanvas.Width = Math.Max(total + 60, 600);
        TreeCanvas.Height = 30 + depth * (h + gapY) + 10;
        Layout(focus, 0, Math.Max(30, (TreeCanvas.Width - total) / 2));
    }

    private void Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        TreeCanvas.Children.Add(e);
    }

    private void AddConnector(Point[] points, bool dashed)
    {
        var fig = new PathFigure { StartPoint = points[0] };
        foreach (var pt in points.Skip(1)) fig.Segments.Add(new LineSegment(pt, true));
        var geo = new PathGeometry([fig]);
        var path = new System.Windows.Shapes.Path
        {
            Data = geo,
            Stroke = UiKit.Brush(dashed ? "Border" : "BorderStrong"),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        if (dashed) path.StrokeDashArray = [4, 3];
        TreeCanvas.Children.Add(path);
    }

    /// <summary>A person on the tree: click to select, double-click to centre the tree on them, right-click for their menu.  A last-column card with parents has an arrow to move the tree up to them.</summary>
    private FrameworkElement PersonCard(Person p, double w, double h, bool isFocus, bool hasMore, string? extraLine = null)
    {
        var selected = _state!.Selected == p;
        var card = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(10),
            Background = UiKit.Brush("Surface"),
            BorderBrush = selected ? UiKit.Brush("Brand") : UiKit.Brush("Border"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            Effect = isFocus ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.12, Color = Colors.Black } : null,
        };
        var grid = new Grid { ClipToBounds = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var strip = new Border { Background = UiKit.SexBrush(p.Sex), CornerRadius = new CornerRadius(9, 0, 0, 9), Margin = new Thickness(selected ? -1 : 0) };
        grid.Children.Add(strip);

        var avatarSize = Math.Min(44, h - 22);
        var avatar = UiKit.Avatar(Doc, p, avatarSize);
        avatar.Margin = new Thickness(10, 0, 8, 0);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(avatar, 1);
        grid.Children.Add(avatar);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(UiKit.Text(p.DisplayName, size: isFocus ? 14.5 : h >= 58 ? 13 : 12.5, weight: FontWeights.SemiBold));
        var span = p.LifeSpan;
        if (span.Length > 0) text.Children.Add(UiKit.Text(span, "Caption"));
        var line3 = extraLine ?? (h >= 64 ? Doc.PlaceName(p.Birth?.PlaceId) : null);
        if (!string.IsNullOrEmpty(line3) && h >= 56) text.Children.Add(UiKit.Text(line3, "Caption", size: 11.5, brush: UiKit.Brush("Subtle")));
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        if (hasMore)
        {
            // More ancestors beyond the last column: one click moves the tree up to this person.
            var more = NavButton("ArrowRight", $"Show {p.DisplayName}'s ancestors", () => _state.Select(p, focus: true));
            more.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(more, 3);
            grid.Children.Add(more);
        }

        card.Child = grid;
        card.ToolTip = Tooltip(p);
        card.MouseEnter += (_, _) => { if (!selected) card.Background = UiKit.Brush("Hover"); };
        card.MouseLeave += (_, _) => card.Background = UiKit.Brush("Surface");
        card.MouseLeftButtonDown += (_, e) =>
        {
            _state.Select(p, focus: e.ClickCount == 2);
            e.Handled = true;
        };
        card.MouseRightButtonUp += (_, e) => { PersonMenu.Show(_state, p, OpenPersonView); e.Handled = true; };
        return card;
    }

    /// <summary>A small round button for moving the tree up or down a generation.</summary>
    private static Button NavButton(string icon, string tooltip, Action click)
    {
        var b = UiKit.IconButton(icon, tooltip, (_, _) => click());
        b.Width = b.Height = 26;
        b.Padding = new Thickness(4);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Cursor = Cursors.Hand;
        return b;
    }

    /// <summary>Down a generation: one child moves the tree at once; several are listed in birth order.</summary>
    private void ChooseChild(FrameworkElement anchor, List<Person> kids)
    {
        if (kids.Count == 1)
        {
            _state!.Select(kids[0], focus: true);
            return;
        }
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var k in kids)
        {
            var id = k.Id;
            var item = new MenuItem { Header = k.LifeSpan.Length > 0 ? $"{k.DisplayName} ({k.LifeSpan})" : k.DisplayName };
            item.Click += (_, _) => _state!.Select(Doc.GetPerson(id), focus: true);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private string Tooltip(Person p)
    {
        var lines = new List<string> { p.DisplayName };
        if (p.Birth is { } b) lines.Add($"Born: {Describe(b)}");
        if (p.Death is { } d) lines.Add($"Died: {Describe(d)}");
        if (Doc.HomePerson is { } home && home != p)
            lines.Add($"Relationship to {home.Given}: {Kinship.Describe(Doc, home, p, withPath: false).Text}");
        return string.Join("\n", lines);
    }

    private string Describe(Fact f)
    {
        var place = Doc.PlaceName(f.PlaceId);
        var date = f.ParsedDate.Display;
        return place.Length == 0 ? date : date.Length == 0 ? place : $"{date}, {place}";
    }

    private static FrameworkElement EmptySlot(string label, double w, double h, Action add)
    {
        var grid = new Grid { Width = w, Height = h, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = label };
        var rect = new Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            Stroke = UiKit.Brush("BorderStrong"),
            StrokeThickness = 1.2,
            StrokeDashArray = [4, 3],
            Fill = Brushes.Transparent,
        };
        grid.Children.Add(rect);
        var content = UiKit.IconText("Plus", label, 14);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        TextElement.SetForeground(content, UiKit.Brush("Muted"));
        grid.Children.Add(content);
        grid.MouseEnter += (_, _) => { rect.Fill = UiKit.Brush("BrandSofter"); rect.Stroke = UiKit.Brush("Brand"); TextElement.SetForeground(content, UiKit.Brush("Brand")); };
        grid.MouseLeave += (_, _) => { rect.Fill = Brushes.Transparent; rect.Stroke = UiKit.Brush("BorderStrong"); TextElement.SetForeground(content, UiKit.Brush("Muted")); };
        grid.MouseLeftButtonUp += (_, e) => { add(); e.Handled = true; };
        return grid;
    }
}

public static class TreeDocumentUiExtensions
{
    /// <summary>"m. 1854" style caption for a couple.</summary>
    public static string? Marriage(this TreeDocument doc, Family fam)
    {
        var m = fam.Marriage;
        var date = m?.ParsedDate.Display ?? "";
        var label = fam.Type switch
        {
            CoupleType.Partner => "Partner",
            CoupleType.Friend => "Friend",
            _ => "m.",
        };
        if (fam.Status is CoupleStatus.Divorced) label = "div.";
        return date.Length > 0 ? $"{label} {date}" : fam.Type == CoupleType.Spouse ? null : label;
    }
}
