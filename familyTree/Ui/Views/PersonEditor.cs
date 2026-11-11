using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Mycelia.Model;
using Microsoft.Win32;

namespace Mycelia.Ui.Views;

/// <summary>The panel on the right of the People workspace: one person's names, facts, family, notes and media.</summary>
public sealed class PersonEditor : UserControl
{
    private readonly AppState _state;
    private Person? _person;
    private Fact? _expanded;
    private int _tab;
    private TextBlock? _headerName;
    private TextBlock? _headerSpan;

    private Border? _warnings;
    private readonly System.Windows.Threading.DispatcherTimer _warningTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public PersonEditor(AppState state)
    {
        _state = state;
        Focusable = false;
        _warningTimer.Tick += (_, _) => { _warningTimer.Stop(); UpdateWarnings(); };
        state.EditStateChanged += () => { _warningTimer.Stop(); _warningTimer.Start(); };
        state.BookmarksChanged += ShowBookmark;
    }

    private Button? _bookmark;

    /// <summary>The header's bookmark button: filled in the brand colour when the person is bookmarked.</summary>
    private void ShowBookmark()
    {
        if (_bookmark is null || _person is null) return;
        var marked = _state.IsBookmarked(_person);
        _bookmark.ToolTip = marked ? "Remove bookmark (Ctrl+Shift+B)" : "Bookmark this person (Ctrl+Shift+B)";
        if (marked) _bookmark.Foreground = UiKit.Brush("Brand");
        else _bookmark.ClearValue(ForegroundProperty);
    }

    /// <summary>Options > Warnings: show the data-error box.</summary>
    public static bool ShowWarnings { get; set; } = true;

    /// <summary>
    /// The data-error check for this one person, shown as they are edited, so problems are seen
    /// where they are made rather than only in the Tools report.
    /// </summary>
    private void UpdateWarnings()
    {
        if (_warnings is null || _person is null) return;
        if (!ShowWarnings)
        {
            _warnings.Visibility = Visibility.Collapsed;
            return;
        }
        var issues = DataChecker.CheckPerson(Doc, _person).DistinctBy(i => i.Message).ToList();
        var problems = issues.Where(i => i.Category != "Missing").ToList();
        var missing = issues.Where(i => i.Category == "Missing").ToList();
        if (problems.Count == 0 && missing.Count == 0)
        {
            _warnings.Visibility = Visibility.Collapsed;
            return;
        }
        _warnings.Visibility = Visibility.Visible;
        _warnings.Background = UiKit.Brush(problems.Count > 0 ? "WarningSoft" : "SurfaceAlt");
        var sp = new StackPanel();
        foreach (var issue in problems)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var ic = UiKit.Icon("Warning", 14, UiKit.Brush("Warning"));
            ic.Margin = new Thickness(0, 1, 8, 0);
            ic.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(ic);
            var t = UiKit.Text(issue.Message, size: 12.5);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            row.Children.Add(t);
            sp.Children.Add(row);
        }
        if (missing.Count > 0)
        {
            var text = "To complete: " + string.Join("  ·  ", missing.Select(m => m.Message.TrimEnd('.')));
            var t = UiKit.Text(text, "Caption");
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            if (problems.Count > 0) t.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(t);
        }
        _warnings.Child = sp;
    }

    private TreeDocument Doc => _state.Doc;

    public void Load(Person? person)
    {
        if (_person is not null) _person.PropertyChanged -= OnPersonChanged;
        if (person != _person) _expanded = null;
        _person = person;
        if (_person is not null) _person.PropertyChanged += OnPersonChanged;
        Rebuild();
    }

    private void OnPersonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_person is null) return;
        if (_headerName is not null) _headerName.Text = _person.DisplayName;
        if (_headerSpan is not null) _headerSpan.Text = SpanText(_person);
    }

    private void Rebuild()
    {
        if (_person is null)
        {
            Content = UiKit.EmptyState("Person", "No one selected", "Choose someone in the index or on the tree to see and edit their details.");
            return;
        }
        var p = _person;
        var root = new DockPanel();
        var header = BuildHeader(p);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl { Style = UiKit.Res<Style>("PanelTabs") };
        tabs.Items.Add(new TabItem { Header = "Facts", Content = Scroll(BuildFactsTab(p)) });
        tabs.Items.Add(new TabItem { Header = "Family", Content = Scroll(BuildFamilyTab(p)) });
        tabs.Items.Add(new TabItem { Header = "Notes", Content = BuildNotesTab(p) });
        tabs.Items.Add(new TabItem { Header = "Media", Content = Scroll(BuildMediaTab(p)) });
        tabs.SelectedIndex = _tab;
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) _tab = tabs.SelectedIndex; };
        root.Children.Add(tabs);
        Content = root;
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private static string SpanText(Person p)
    {
        var span = p.LifeSpan;
        var sex = p.Sex switch { Sex.Male => "Male", Sex.Female => "Female", _ => "Sex unknown" };
        var parts = new List<string>();
        if (span.Length > 0) parts.Add(span);
        parts.Add(sex);
        if (AgeOf(p) is int age) parts.Add($"Age {age}");
        return string.Join("   ·   ", parts);
    }

    /// <summary>Age at death, or today's age for someone with no death recorded (not over the 120-year limit).</summary>
    private static int? AgeOf(Person p)
    {
        var b = p.Birth?.ParsedDate;
        if (b is not { IsValid: true, IsRange: false }) return null;
        var d = p.Death?.ParsedDate;
        if (d is { IsValid: true, IsRange: false }) return Age.YearsBetween(b.Start, d.Start);
        if (p.Death is not null) return null;
        var now = DateTime.Today;
        var age = Age.YearsBetween(b.Start, new DatePart(now.Year, now.Month, now.Day));
        return age is >= 0 and <= DataChecker.QuestionableDeathAge ? age : null;
    }

    // ---------------------------------------------------------------- header

    private FrameworkElement BuildHeader(Person p)
    {
        var grid = new Grid { Margin = new Thickness(16, 16, 12, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = UiKit.Avatar(Doc, p, 60);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(avatar);

        var text = new StackPanel { Margin = new Thickness(14, 2, 8, 0) };
        _headerName = UiKit.Text(p.DisplayName, "H2");
        _headerSpan = UiKit.Text(SpanText(p), "Caption");
        _headerSpan.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(_headerName);
        text.Children.Add(_headerSpan);
        var home = Doc.HomePerson;
        var relation = home is null ? "" : home == p ? "Home person" : Kinship.Describe(Doc, home, p, withPath: false).Text;
        if (relation.Length > 0)
        {
            var label = home == p ? "Home person" : $"{Capitalise(relation)} of {home!.DisplayName}";
            var badge = new Border { Style = UiKit.Res<Style>("Badge"), Margin = new Thickness(0, 7, 0, 0) };
            var bt = UiKit.Text(label, size: 11.5, brush: UiKit.Brush("Brand"), weight: FontWeights.Medium);
            badge.Child = bt;
            badge.ToolTip = label;
            text.Children.Add(badge);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var star = UiKit.IconButton("Star", home == p ? "This is the home person" : "Make this the home person", (_, _) => PersonActions.SetHome(_state, p));
        if (home == p) star.Foreground = UiKit.Brush("Warning");
        actions.Children.Add(star);
        _bookmark = UiKit.IconButton("Bookmark", "", (_, _) => _state.ToggleBookmark(p));
        ShowBookmark();
        actions.Children.Add(_bookmark);
        actions.Children.Add(UiKit.IconButton("Link", "Merge with another record of the same person...", (_, _) => PersonActions.Merge(_state, p, null)));
        actions.Children.Add(UiKit.IconButton("Trash", "Delete this person", (_, _) => PersonActions.Delete(_state, p)));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------------------------------------------------------------- facts

    private FrameworkElement BuildFactsTab(Person p)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 20) };
        _warnings = new Border
        {
            Background = UiKit.Brush("WarningSoft"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 14),
        };
        sp.Children.Add(_warnings);
        UpdateWarnings();
        sp.Children.Add(UiKit.Text("NAME", "SectionLabel"));

        var names = new Grid();
        names.ColumnDefinitions.Add(new ColumnDefinition());
        names.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        names.ColumnDefinitions.Add(new ColumnDefinition());
        names.RowDefinitions.Add(new RowDefinition());
        names.RowDefinitions.Add(new RowDefinition());
        var given = UiKit.Field(Doc, "Given names", () => p.Given, v => p.Given = v);
        var surname = UiKit.Field(Doc, "Surname", () => p.Surname, v => p.Surname = v);
        var suffix = UiKit.Field(Doc, "Suffix", () => p.Suffix, v => p.Suffix = v);
        Grid.SetColumn(surname, 2);
        Grid.SetRow(suffix, 1);
        names.Children.Add(given);
        names.Children.Add(surname);
        names.Children.Add(suffix);

        var sexPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sexPanel.Children.Add(new TextBlock { Text = "Sex", Style = UiKit.Res<Style>("FieldLabel") });
        var sexBox = new ComboBox { ItemsSource = Enum.GetValues<Sex>(), SelectedItem = p.Sex };
        sexBox.SelectionChanged += (_, _) =>
        {
            if (sexBox.SelectedItem is Sex s && s != p.Sex) Doc.Change(() => p.Sex = s, structural: false);
        };
        sexPanel.Children.Add(sexBox);
        Grid.SetRow(sexPanel, 1);
        Grid.SetColumn(sexPanel, 2);
        names.Children.Add(sexPanel);
        sp.Children.Add(names);

        var head = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var add = UiKit.Button("GhostButton", "Add fact", (s, _) => ShowAddFactMenu((Button)s, p.Facts, FactScope.Person, p), "Plus");
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(new TextBlock { Text = "FACTS", Style = UiKit.Res<Style>("SectionLabel"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) });
        sp.Children.Add(head);

        sp.Children.Add(BuildFactList(p.Facts, p, p));
        return sp;
    }

    private void ShowAddFactMenu(Button anchor, List<Fact> facts, FactScope scope, Person? person) =>
        FactForm.ShowAddMenu(Doc, anchor, facts, scope, person, created =>
        {
            _expanded = created;
            Rebuild();
        });

    private FrameworkElement BuildFactList(List<Fact> facts, Person? person, object owner)
    {
        var list = new StackPanel();
        // The facts table's order (Design considerations, Person view).
        var ordered = FactOrder.Sorted(facts).ToList();
        if (ordered.Count == 0)
        {
            var none = UiKit.Text("No facts yet.", "Caption");
            none.Margin = new Thickness(0, 2, 0, 8);
            list.Children.Add(none);
        }
        foreach (var f in ordered) list.Children.Add(FactRow(f, facts, person, owner));
        return list;
    }

    private FrameworkElement FactRow(Fact f, List<Fact> facts, Person? person, object owner)
    {
        var expanded = _expanded == f;
        var outer = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = expanded ? UiKit.Brush("BorderStrong") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Background = expanded ? UiKit.Brush("SurfaceAlt") : Brushes.Transparent,
            Margin = new Thickness(-8, 0, -8, 2),
        };
        var stack = new StackPanel();
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(8, 7, 8, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        var title = UiKit.Text(f.Type.Name, weight: FontWeights.SemiBold);
        titleLine.Children.Add(title);
        if (!f.Preferred && facts.Count(x => x.TypeId == f.TypeId) > 1)
            titleLine.Children.Add(UiKit.Text("  alternate", "Caption", size: 11));
        if (f.Private)
            titleLine.Children.Add(UiKit.Text("  private", "Caption", size: 11, brush: UiKit.Brush("Warning")));
        left.Children.Add(titleLine);
        var detail = string.Join("  ·  ", new[] { Doc.PlaceName(f.PlaceId), f.Description }.Where(s => s.Length > 0));
        var detailText = UiKit.Text(detail, "Caption");
        if (detail.Length > 0) left.Children.Add(detailText);
        if (f.Citations.Count > 0)
        {
            var src = UiKit.IconText("Book", f.Citations.Count == 1 ? "1 source" : $"{f.Citations.Count} sources", 12);
            TextElement.SetForeground(src, UiKit.Brush("Brand"));
            TextElement.SetFontSize(src, 11.5);
            src.Margin = new Thickness(0, 3, 0, 0);
            left.Children.Add(src);
        }
        row.Children.Add(left);

        var dateText = UiKit.Text(f.ParsedDate.Display, brush: f.ParsedDate.IsValid || f.ParsedDate.IsEmpty ? UiKit.Brush("Ink") : UiKit.Brush("Warning"));
        dateText.Margin = new Thickness(10, 0, 0, 0);
        dateText.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(dateText, 1);
        row.Children.Add(dateText);

        row.MouseEnter += (_, _) => { if (_expanded != f) outer.Background = UiKit.Brush("Hover"); };
        row.MouseLeave += (_, _) => { if (_expanded != f) outer.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            _expanded = _expanded == f ? null : f;
            Rebuild();
        };
        stack.Children.Add(row);

        if (expanded)
        {
            stack.Children.Add(FactEditor(f, facts, person, owner, title, detailText, dateText));
        }
        outer.Child = stack;
        return outer;
    }

    private FrameworkElement FactEditor(Fact f, List<Fact> facts, Person? person, object owner, TextBlock title, TextBlock detailText, TextBlock dateText)
    {
        void Changed()
        {
            title.Text = f.Type.Name;
            detailText.Text = FactForm.Detail(Doc, f);
            dateText.Text = f.ParsedDate.Display;
            dateText.Foreground = f.ParsedDate.IsValid || f.ParsedDate.IsEmpty ? UiKit.Brush("Ink") : UiKit.Brush("Warning");
        }
        var form = FactForm.Build(Doc, f, facts, person, Changed, Rebuild, () => { _expanded = null; Rebuild(); });
        form.Margin = new Thickness(10, 2, 10, 12);
        return form;
    }

    // ---------------------------------------------------------------- family

    private FrameworkElement PersonRow(Person p, string? caption = null, UIElement? trailing = null)
    {
        var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var av = UiKit.Avatar(Doc, p, 32);
        grid.Children.Add(av);
        var t = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(UiKit.Text(p.DisplayName, weight: FontWeights.Medium));
        var cap = string.Join("   ·   ", new[] { p.LifeSpan, caption ?? "" }.Where(s => s.Length > 0));
        if (cap.Length > 0) t.Children.Add(UiKit.Text(cap, "Caption"));
        Grid.SetColumn(t, 1);
        grid.Children.Add(t);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
        t.MouseLeftButtonUp += (_, _) => _state.Select(p, focus: true);
        av.MouseLeftButtonUp += (_, _) => _state.Select(p, focus: true);
        t.ToolTip = "Go to " + p.DisplayName;
        return grid;
    }

    private static ComboBox EnumBox<T>(T value, Action<T> set, double width = 112) where T : struct, Enum
    {
        var box = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T v && !EqualityComparer<T>.Default.Equals(v, value)) { value = v; set(v); } };
        return box;
    }

    private FrameworkElement BuildFamilyTab(Person p)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 20) };

        // Parents
        sp.Children.Add(UiKit.Text("PARENTS", "SectionLabel"));
        var parentFamilies = Doc.ParentFamilies(p).ToList();
        foreach (var fam in parentFamilies)
        {
            var link = fam.Children.First(c => c.PersonId == p.Id);
            foreach (var (pid, first) in new[] { (fam.Partner1Id, true), (fam.Partner2Id, false) })
            {
                var parent = Doc.GetPerson(pid);
                if (parent is null) continue;
                var role = parent.Sex switch { Sex.Male => "Father", Sex.Female => "Mother", _ => "Parent" };
                var nature = EnumBox(first ? link.Nature1 : link.Nature2, v => Doc.Change(() => { if (first) link.Nature1 = v; else link.Nature2 = v; }, structural: false), 100);
                nature.ToolTip = $"How {p.Given} is related to this parent";
                sp.Children.Add(PersonRow(parent, role, nature));
            }
            var parentActions = new WrapPanel();
            // With two or more sets of parents, the preferred set (listed first) is marked and the others can be chosen;
            // the pedigree, charts and Father and Mother follow it (TreeDocument.ParentFamilies).
            if (parentFamilies.Count > 1)
            {
                if (fam == parentFamilies[0])
                {
                    var preferred = UiKit.Text("Preferred", "Caption");
                    preferred.VerticalAlignment = VerticalAlignment.Center;
                    preferred.Margin = new Thickness(0, 0, 12, 0);
                    parentActions.Children.Add(preferred);
                }
                else
                {
                    var famId = fam.Id;
                    parentActions.Children.Add(UiKit.Button("GhostButton", "Make preferred", (_, _) =>
                        Doc.Change(() => { if (Doc.GetPerson(p.Id) is { } person) person.PreferredParentsId = famId; }), "Check"));
                }
            }
            var detach = UiKit.Button("GhostButton", "Detach from these parents", (_, _) =>
            {
                if (Dialogs.Confirm("Detach from parents", $"Remove the link between {p.DisplayName} and {Doc.CoupleName(fam)}?  Nobody is deleted.", "Detach"))
                    Doc.Change(() => Doc.DetachChild(fam, p));
            }, "Unlink");
            detach.Foreground = UiKit.Brush("Muted");
            parentActions.Children.Add(detach);
            sp.Children.Add(parentActions);
        }
        var parentButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (Doc.Father(p) is null) parentButtons.Children.Add(UiKit.Button("GhostButton", "Add father", (_, _) => PersonActions.AddParent(_state, p, Sex.Male), "Plus"));
        if (Doc.Mother(p) is null) parentButtons.Children.Add(UiKit.Button("GhostButton", "Add mother", (_, _) => PersonActions.AddParent(_state, p, Sex.Female), "Plus"));
        parentButtons.Children.Add(UiKit.Button("GhostButton", "Link existing parent", (_, _) => PersonActions.LinkExistingParent(_state, p), "Link"));
        sp.Children.Add(parentButtons);

        sp.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });

        // Spouses and children
        sp.Children.Add(UiKit.Text("SPOUSES AND CHILDREN", "SectionLabel"));
        // A spouse order set by hand (Set spouse order) wins; otherwise marriages are in date order.
        var families = p.SpouseOrder.Count > 0 ? Doc.SpouseFamilies(p).ToList() : FactOrder.ByMarriage(Doc.SpouseFamilies(p)).ToList();
        foreach (var fam in families)
        {
            var box = new Border { Style = UiKit.Res<Style>("Card"), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
            var inner = new StackPanel();
            var spouse = Doc.GetPerson(fam.OtherPartner(p.Id));
            if (spouse is not null)
            {
                inner.Children.Add(PersonRow(spouse, Doc.Marriage(fam)));
            }
            else
            {
                // The card's own buttons fill this family, not the first with a partner missing.
                inner.Children.Add(UiKit.Text("Unknown spouse", "Caption"));
                var fill = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
                fill.Children.Add(UiKit.Button("GhostButton", "Add spouse", (_, _) => PersonActions.AddSpouse(_state, p, fam), "Plus"));
                fill.Children.Add(UiKit.Button("GhostButton", "Link existing spouse", (_, _) => PersonActions.LinkExistingSpouse(_state, p, fam), "Link"));
                inner.Children.Add(fill);
            }

            var typeRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            var typeStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            typeStack.Children.Add(new TextBlock { Text = "Relationship", Style = UiKit.Res<Style>("FieldLabel") });
            typeStack.Children.Add(EnumBox(fam.Type, v => Doc.Change(() => fam.Type = v, structural: false), 130));
            var statusStack = new StackPanel();
            statusStack.Children.Add(new TextBlock { Text = "Status", Style = UiKit.Res<Style>("FieldLabel") });
            statusStack.Children.Add(EnumBox(fam.Status, v => Doc.Change(() => fam.Status = v, structural: false), 130));
            typeRow.Children.Add(typeStack);
            typeRow.Children.Add(statusStack);
            inner.Children.Add(typeRow);

            var factHead = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
            var addFact = UiKit.Button("GhostButton", "Add", (s, _) => ShowAddFactMenu((Button)s, fam.Facts, FactScope.Couple, null), "Plus");
            DockPanel.SetDock(addFact, Dock.Right);
            factHead.Children.Add(addFact);
            factHead.Children.Add(new TextBlock { Text = "COUPLE FACTS", Style = UiKit.Res<Style>("SectionLabel"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) });
            inner.Children.Add(factHead);
            inner.Children.Add(BuildFactList(fam.Facts, null, fam));

            var kids = Doc.Children(fam).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
            var kidHead = new TextBlock { Text = kids.Count == 0 ? "CHILDREN" : $"CHILDREN ({kids.Count})", Style = UiKit.Res<Style>("SectionLabel"), Margin = new Thickness(0, 8, 0, 6) };
            inner.Children.Add(kidHead);
            var isFirst = fam.Partner1Id == p.Id;
            foreach (var kid in kids)
            {
                var link = fam.Children.First(c => c.PersonId == kid.Id);
                var trailing = new StackPanel { Orientation = Orientation.Horizontal };
                var nature = EnumBox(isFirst ? link.Nature1 : link.Nature2, v => Doc.Change(() => { if (isFirst) link.Nature1 = v; else link.Nature2 = v; }, structural: false), 96);
                nature.ToolTip = $"How {kid.Given} is related to {p.Given}";
                trailing.Children.Add(nature);
                var rm = UiKit.IconButton("Unlink", "Detach this child", (_, _) =>
                {
                    if (Dialogs.Confirm("Detach child", $"Remove {kid.DisplayName} from this family?  Nobody is deleted.", "Detach"))
                        Doc.Change(() => Doc.DetachChild(fam, kid));
                });
                rm.Width = rm.Height = 28;
                rm.Margin = new Thickness(4, 0, 0, 0);
                trailing.Children.Add(rm);
                inner.Children.Add(PersonRow(kid, null, trailing));
            }
            var kidButtons = new WrapPanel();
            kidButtons.Children.Add(UiKit.Button("GhostButton", "Add child", (_, _) => PersonActions.AddChild(_state, p, fam), "Plus"));
            kidButtons.Children.Add(UiKit.Button("GhostButton", "Link existing child", (_, _) => PersonActions.LinkExistingChild(_state, p, fam), "Link"));
            if (spouse is not null)
            {
                var detach = UiKit.Button("GhostButton", "End this link", (_, _) =>
                {
                    if (Dialogs.Confirm("Remove relationship", $"Remove {spouse.DisplayName} as spouse of {p.DisplayName}?  Nobody is deleted; any children stay with {p.Given}.", "Remove"))
                        Doc.Change(() => Doc.DetachPartner(fam, spouse));
                }, "Unlink");
                detach.Foreground = UiKit.Brush("Muted");
                kidButtons.Children.Add(detach);
            }
            inner.Children.Add(kidButtons);
            box.Child = inner;
            sp.Children.Add(box);
        }
        var spouseButtons = new WrapPanel();
        spouseButtons.Children.Add(UiKit.Button("GhostButton", "Add spouse", (_, _) => PersonActions.AddSpouse(_state, p), "Plus"));
        spouseButtons.Children.Add(UiKit.Button("GhostButton", "Link existing spouse", (_, _) => PersonActions.LinkExistingSpouse(_state, p), "Link"));
        if (families.Count == 0)
            spouseButtons.Children.Add(UiKit.Button("GhostButton", "Add child", (_, _) => PersonActions.AddChild(_state, p), "Plus"));
        if (families.Count > 1)
            spouseButtons.Children.Add(UiKit.Button("GhostButton", "Set spouse order...", (_, _) => Dialogs.SpouseOrder(_state, p), "Rings"));
        sp.Children.Add(spouseButtons);

        sp.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });

        // Siblings
        var siblings = Doc.Siblings(p).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
        sp.Children.Add(UiKit.Text(siblings.Count == 0 ? "SIBLINGS" : $"SIBLINGS ({siblings.Count})", "SectionLabel"));
        foreach (var s in siblings) sp.Children.Add(PersonRow(s, Kinship.Describe(Doc, p, s, withPath: false).Text));
        var addSib = UiKit.Button("GhostButton", "Add sibling", (_, _) => PersonActions.AddSibling(_state, p), "Plus");
        addSib.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(addSib);
        return sp;
    }

    // ---------------------------------------------------------------- notes and media

    private FrameworkElement BuildNotesTab(Person p)
    {
        var dock = new DockPanel { Margin = new Thickness(16, 14, 16, 16) };
        var label = UiKit.Text("NOTES", "SectionLabel");
        DockPanel.SetDock(label, Dock.Top);
        dock.Children.Add(label);
        var box = new TextBox
        {
            Text = p.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 8, 10, 8),
        };
        var token = new object();
        box.GotKeyboardFocus += (_, _) => token = new object();
        box.TextChanged += (_, _) =>
        {
            if (box.Text == p.Notes) return;
            Doc.BeginEdit(token);
            p.Notes = box.Text;
            Doc.MarkEdited();
        };
        box.LostKeyboardFocus += (_, _) => Doc.EndEdit();
        dock.Children.Add(box);
        return dock;
    }

    private FrameworkElement BuildMediaTab(Person p)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 20) };
        sp.Children.Add(UiKit.Text("PORTRAIT", "SectionLabel"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row.Children.Add(UiKit.Avatar(Doc, p, 96));
        var buttons = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(UiKit.Button("SecondaryButton", "Choose picture...", (_, _) => ChoosePortrait(p), "Image"));
        if (p.PhotoMediaId is not null)
        {
            var rm = UiKit.Button("GhostButton", "Remove portrait", (_, _) => { Doc.Change(() => p.PhotoMediaId = null); }, "Close");
            rm.Margin = new Thickness(0, 6, 0, 0);
            rm.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Children.Add(rm);
        }
        row.Children.Add(buttons);
        sp.Children.Add(row);

        var linked = Doc.Data.Media.Where(m => m.PersonIds.Contains(p.Id)).ToList();
        sp.Children.Add(UiKit.Text(linked.Count == 0 ? "LINKED MEDIA" : $"LINKED MEDIA ({linked.Count})", "SectionLabel"));
        var wrap = new WrapPanel();
        foreach (var m in linked)
        {
            var tile = new StackPanel { Width = 108, Margin = new Thickness(0, 0, 10, 10) };
            var frame = new Border { Height = 84, CornerRadius = new CornerRadius(8), Background = UiKit.Brush("SurfaceAlt"), BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1), ClipToBounds = true };
            var img = m.IsImage ? UiKit.LoadImage(m.FilePath, 330) : null;
            frame.Child = img is not null
                ? new Image { Source = img, Stretch = Stretch.UniformToFill }
                : UiKit.Icon(m.IsImage ? "Image" : "Note", 28, UiKit.Brush("Subtle"));
            tile.Children.Add(frame);
            tile.Children.Add(UiKit.Text(m.Caption.Length > 0 ? m.Caption : Path.GetFileName(m.FilePath), "Caption"));
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = m.FilePath;
            tile.MouseLeftButtonUp += (_, _) => _state.Navigate("Media");
            wrap.Children.Add(tile);
        }
        sp.Children.Add(wrap);
        var add = UiKit.Button("GhostButton", "Link a media file...", (_, _) => AddMedia(p, portrait: false), "Plus");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(add);
        return sp;
    }

    private void ChoosePortrait(Person p) => AddMedia(p, portrait: true);

    private void AddMedia(Person p, bool portrait)
    {
        var dlg = new OpenFileDialog
        {
            Title = portrait ? "Choose a portrait" : "Link a media file",
            Filter = portrait
                ? "Pictures (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff"
                : "All media|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.pdf;*.mp3;*.wav;*.mp4;*.txt;*.doc;*.docx|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        Doc.Change(() =>
        {
            var media = Doc.Data.Media.FirstOrDefault(m => string.Equals(m.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase));
            if (media is null)
            {
                media = new MediaItem { FilePath = dlg.FileName, Caption = Path.GetFileNameWithoutExtension(dlg.FileName) };
                Doc.Data.Media.Add(media);
            }
            if (!media.PersonIds.Contains(p.Id)) media.PersonIds.Add(p.Id);
            if (portrait) p.PhotoMediaId = media.Id;
        });
    }
}
