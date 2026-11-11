using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>The Edit and Tools windows: find and replace, spouse order, fact types, relationships, historical events, export and name options.</summary>
public static partial class Dialogs
{
    private static Border Framed(UIElement child, double height) => new()
    {
        Child = child,
        Height = height,
        BorderBrush = UiKit.Brush("Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(4),
    };

    private static TextBlock Note(string text, double top = 10)
    {
        var t = UiKit.Text(text, "Caption");
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        t.Margin = new Thickness(0, top, 0, 0);
        return t;
    }

    // ---------------------------------------------------------------- find and replace

    /// <summary>Edit > Find and Replace (Ctrl+F): every text field holding the words, then replace in the ticked ones.</summary>
    public static void FindAndReplace(AppState state)
    {
        var doc = state.Doc;
        var w = MakeWindow("Find and replace", 720);
        var body = new StackPanel();
        var (fp, find) = Labelled("Find");
        var (rp, replace) = Labelled("Replace with");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(fp);
        Grid.SetColumn(rp, 2);
        row.Children.Add(rp);
        body.Children.Add(row);
        var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var matchCase = new CheckBox { Content = "Match case", Margin = new Thickness(0, 0, 20, 0) };
        var whole = new CheckBox { Content = "Whole words only", Margin = new Thickness(0, 0, 20, 0) };
        var search = UiKit.Button("SecondaryButton", "Find", (_, _) => { }, "Search");
        opts.Children.Add(matchCase);
        opts.Children.Add(whole);
        opts.Children.Add(search);
        body.Children.Add(opts);
        var summary = UiKit.Text("Type what to look for and choose Find.", "Caption");
        summary.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(summary);
        var list = new StackPanel();
        body.Children.Add(Framed(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 300));
        body.Children.Add(Note("Names, fact descriptions and notes, citation details, place names, sources and media captions are searched.  Untick anything that should stay as it is."));

        List<TreeTools.TextHit> hits = [];
        void Search()
        {
            hits = TreeTools.Find(doc, find.Text, matchCase.IsChecked == true, whole.IsChecked == true);
            list.Children.Clear();
            summary.Text = find.Text.Length == 0 ? "Type what to look for." : hits.Count == 0 ? "Nothing found." : hits.Count == 1 ? "1 place found." : $"{hits.Count} places found.";
            foreach (var h in hits)
            {
                var text = h.Text.Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 90) text = text[..90] + "...";
                var cb = new CheckBox { IsChecked = true, Margin = new Thickness(6, 3, 6, 3) };
                var content = new StackPanel();
                content.Children.Add(UiKit.Text($"{h.Owner}  ·  {h.Field}", size: 12, weight: FontWeights.Medium));
                content.Children.Add(UiKit.Text(text, "Caption"));
                cb.Content = content;
                var hit = h;
                cb.Click += (_, _) => hit.Selected = cb.IsChecked == true;
                list.Children.Add(cb);
            }
        }
        search.Click += (_, _) => Search();
        find.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Search(); e.Handled = true; } };

        // Results belong to one search: changing what to look for clears them, so Replace never works
        // from an old list (it searches again first when the list is empty).
        void Stale()
        {
            if (hits.Count == 0 && list.Children.Count == 0) return;
            hits = [];
            list.Children.Clear();
            summary.Text = "Choose Find to search again.";
        }
        find.TextChanged += (_, _) => Stale();
        matchCase.Click += (_, _) => Stale();
        whole.Click += (_, _) => Stale();

        bool Validate()
        {
            if (hits.Count == 0) Search();
            if (hits.Any(h => h.Selected)) return true;
            MessageBox.Show(w, "Nothing to replace.  Find something first.", "Find and replace", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!Run(w, "Find and replace", "Search the whole tree and replace text in one step (Undo reverses it).", body, "Replace ticked", Validate, find)) return;
        var count = 0;
        doc.Change(() => count = TreeTools.Replace(hits, find.Text, replace.Text, matchCase.IsChecked == true, whole.IsChecked == true));
        MessageBox.Show(Application.Current.MainWindow, count == 1 ? "1 field changed." : $"{count} fields changed.", "Find and replace", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- spouse order

    /// <summary>Person > Set Spouse Order: the first spouse is the preferred one.</summary>
    public static void SpouseOrder(AppState state, Person p)
    {
        var doc = state.Doc;
        var families = doc.SpouseFamilies(p).ToList();
        if (families.Count < 2)
        {
            MessageBox.Show(Application.Current.MainWindow, $"{p.DisplayName} has {(families.Count == 0 ? "no spouses" : "one spouse")}, so there is no order to set.", "Set spouse order", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = MakeWindow("Set spouse order", 520);
        var list = new ListBox { Style = UiKit.Res<Style>("PlainList"), Height = 220 };
        void Fill(int select)
        {
            list.Items.Clear();
            for (var i = 0; i < families.Count; i++)
            {
                var f = families[i];
                var spouse = doc.GetPerson(f.OtherPartner(p.Id));
                var row = new DockPanel();
                var pref = UiKit.Text(i == 0 ? "Preferred" : "", "Caption", brush: UiKit.Brush("Brand"));
                DockPanel.SetDock(pref, Dock.Right);
                row.Children.Add(pref);
                var sp = new StackPanel();
                sp.Children.Add(UiKit.Text(spouse?.DisplayName ?? "Unknown spouse", weight: FontWeights.Medium));
                sp.Children.Add(UiKit.Text(f.Marriage is { } m && !m.ParsedDate.IsEmpty ? "Married " + m.ParsedDate.Display : "No marriage date recorded", "Caption"));
                row.Children.Add(sp);
                list.Items.Add(new ListBoxItem { Content = row, Style = UiKit.Res<Style>("ListRow") });
            }
            list.SelectedIndex = select;
        }
        Fill(0);
        void Move(int delta)
        {
            var i = list.SelectedIndex;
            var j = i + delta;
            if (i < 0 || j < 0 || j >= families.Count) return;
            (families[i], families[j]) = (families[j], families[i]);
            Fill(j);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(UiKit.Button("SecondaryButton", "Move up", (_, _) => Move(-1)));
        var down = UiKit.Button("SecondaryButton", "Move down", (_, _) => Move(1));
        down.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(down);
        var body = new StackPanel();
        body.Children.Add(Framed(list, 230));
        body.Children.Add(buttons);
        body.Children.Add(Note("The first spouse is the preferred one: shown first in the tree, the family panel and charts."));
        if (!Run(w, "Set spouse order", $"Spouses of {p.DisplayName}", body, "OK")) return;
        var ids = families.Select(f => f.Id).ToList();
        doc.Change(() => doc.GetPerson(p.Id)!.SpouseOrder = ids);
    }

    // ---------------------------------------------------------------- fact types

    /// <summary>Edit > Manage Facts: the built-in fact types, and the tree's own ones to add, rename or remove.</summary>
    public static void ManageFactTypes(AppState state)
    {
        var doc = state.Doc;
        var w = MakeWindow("Manage fact types", 760);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var list = new ListBox { Style = UiKit.Res<Style>("PlainList") };
        grid.Children.Add(Framed(list, 380));
        var editor = new StackPanel();
        Grid.SetColumn(editor, 2);
        grid.Children.Add(editor);

        int Uses(int id) => doc.AllFacts().Count(x => x.Fact.TypeId == id);
        void Fill(int? select)
        {
            list.Items.Clear();
            foreach (var t in FactTypes.PersonTypes.Concat(FactTypes.CoupleTypes).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = new DockPanel();
                var uses = Uses(t.Id);
                var right = UiKit.Text(uses == 0 ? "" : uses.ToString(), "Caption");
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                var sp = new StackPanel();
                sp.Children.Add(UiKit.Text(t.Name, weight: FactTypes.IsCustom(t.Id) ? FontWeights.SemiBold : FontWeights.Normal));
                sp.Children.Add(UiKit.Text($"{(t.Scope == FactScope.Person ? "Individual" : "Shared (couple)")}  ·  {(FactTypes.IsCustom(t.Id) ? "your own" : "built in")}", "Caption"));
                row.Children.Add(sp);
                var item = new ListBoxItem { Content = row, Tag = t.Id, Style = UiKit.Res<Style>("ListRow") };
                list.Items.Add(item);
                if (t.Id == select) list.SelectedItem = item;
            }
            ShowEditor();
        }

        void ShowEditor()
        {
            editor.Children.Clear();
            var id = (list.SelectedItem as ListBoxItem)?.Tag as int?;
            var add = UiKit.Button("PrimaryButton", "New fact type", (_, _) =>
            {
                var name = Prompt("New fact type", "Name (for example \"Apprenticeship\")", "");
                if (name is null) return;
                var newId = TreeImport.NextCustomId(doc.Data);
                doc.Change(() => doc.Data.CustomFactTypes.Add(new CustomFactType { Id = newId, Name = name }), structural: false);
                Fill(newId);
            }, "Plus");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 0, 0, 16);
            editor.Children.Add(add);
            if (id is null) { editor.Children.Add(Note("Choose a fact type to see it, or make a new one.", 0)); return; }
            var type = FactTypes.Get(id.Value);
            editor.Children.Add(UiKit.Text(type.Name, "H3"));
            editor.Children.Add(Note($"Used {Uses(type.Id)} times.", 4));
            var custom = doc.Data.CustomFactTypes.FirstOrDefault(c => c.Id == id);
            if (custom is null)
            {
                editor.Children.Add(Note($"{(type.HasDate ? "Has a date and place." : "A value only.")}  Built-in types cannot be changed; GEDCOM tag {type.Tag}."));
                return;
            }
            var (np, nameBox) = Labelled("Name", custom.Name);
            np.Margin = new Thickness(0, 12, 0, 12);
            nameBox.LostKeyboardFocus += (_, _) =>
            {
                var n = nameBox.Text.Trim();
                if (n.Length == 0 || n == custom.Name) return;
                doc.Change(() => custom.Name = n, structural: false);
                Fill(custom.Id);
            };
            editor.Children.Add(np);
            var scope = new ComboBox { ItemsSource = new[] { "Individual", "Shared (couple)" }, SelectedIndex = custom.Scope == FactScope.Person ? 0 : 1, IsEnabled = Uses(custom.Id) == 0, Margin = new Thickness(0, 0, 0, 12) };
            scope.SelectionChanged += (_, _) => { doc.Change(() => custom.Scope = scope.SelectedIndex == 0 ? FactScope.Person : FactScope.Couple, structural: false); Fill(custom.Id); };
            editor.Children.Add(new TextBlock { Text = "Belongs to", Style = UiKit.Res<Style>("FieldLabel") });
            editor.Children.Add(scope);
            var dated = new CheckBox { Content = "Has a date and place", IsChecked = custom.HasDate, Margin = new Thickness(0, 0, 0, 12) };
            dated.Click += (_, _) => doc.Change(() => { custom.HasDate = custom.HasPlace = dated.IsChecked == true; }, structural: false);
            editor.Children.Add(dated);
            var del = UiKit.Button("GhostButton", "Delete this type", (_, _) =>
            {
                if (Uses(custom.Id) > 0)
                {
                    MessageBox.Show(w, "This type is used by facts.  Change or delete those facts first.", "Manage fact types", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                doc.Change(() => doc.Data.CustomFactTypes.Remove(custom), structural: false);
                Fill(null);
            }, "Trash");
            del.Foreground = UiKit.Brush("Danger");
            del.HorizontalAlignment = HorizontalAlignment.Left;
            editor.Children.Add(del);
            editor.Children.Add(Note("Your own types are written to GEDCOM as EVEN (or FACT, for a value only) with a TYPE line naming them, and read back the same way."));
        }

        list.SelectionChanged += (_, _) => ShowEditor();
        Fill(null);
        Run(w, "Manage fact types", "Built-in types, and your own.  Changes take effect at once; Undo reverses them.", grid, "Close");
    }

    // ---------------------------------------------------------------- relationships

    /// <summary>Edit > Manage Relationships: every couple in the tree.  Returns the couple chosen to go to.</summary>
    public static Family? Relationships(AppState state)
    {
        var doc = state.Doc;
        var w = MakeWindow("Relationships", 820);
        var body = new DockPanel();
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top);
        body.Children.Add(search);
        var list = new ListBox { Style = UiKit.Res<Style>("PlainList") };
        var all = doc.Data.Families
            .Where(f => f.Partner1Id is not null || f.Partner2Id is not null)
            .OrderBy(f => doc.GetPerson(f.Partner1Id)?.IndexName ?? doc.GetPerson(f.Partner2Id)?.IndexName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var count = UiKit.Text("", "Caption");
        void Fill()
        {
            var q = search.Text.Trim();
            list.Items.Clear();
            var shown = 0;
            foreach (var f in all)
            {
                var a = doc.GetPerson(f.Partner1Id);
                var b = doc.GetPerson(f.Partner2Id);
                var names = $"{a?.IndexName ?? "Unknown"}  &  {b?.IndexName ?? "Unknown"}";
                if (q.Length > 0 && !names.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
                shown++;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                void Cell(string text, int col, bool strong = false)
                {
                    var t = strong ? UiKit.Text(text, weight: FontWeights.Medium) : UiKit.Text(text, "Caption");
                    t.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(t, col);
                    g.Children.Add(t);
                }
                Cell(names, 0, true);
                Cell(f.Type.ToString(), 1);
                Cell(f.Status.ToString(), 2);
                Cell(f.Marriage?.ParsedDate.Display ?? "", 3);
                Cell(f.Children.Count == 1 ? "1 child" : $"{f.Children.Count} children", 4);
                list.Items.Add(new ListBoxItem { Content = g, Tag = f, Style = UiKit.Res<Style>("ListRow") });
            }
            count.Text = shown == all.Count ? $"{all.Count} relationships" : $"{shown} of {all.Count} relationships";
        }
        search.TextChanged += (_, _) => Fill();
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) w.DialogResult = true; };
        Fill();
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(count);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(Framed(list, 380));
        return Run(w, "Relationships", "Every couple in the tree, with type, status and marriage.  Choose one to go to it.", body, "Go to", () => list.SelectedItem is not null, search)
            ? (list.SelectedItem as ListBoxItem)?.Tag as Family
            : null;
    }

    // ---------------------------------------------------------------- historical events

    /// <summary>Edit > Manage Historical Events: the events shown on timelines.</summary>
    public static void HistoricalEvents(AppState state)
    {
        var doc = state.Doc;
        var w = MakeWindow("Historical events", 820);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var list = new ListBox { Style = UiKit.Res<Style>("PlainList") };
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(HistoricalEvent.Title)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        var date = new FrameworkElementFactory(typeof(TextBlock));
        date.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new EventLineConverter(),
            Bindings = { new Binding(nameof(HistoricalEvent.Date)), new Binding(nameof(HistoricalEvent.EndDate)), new Binding(nameof(HistoricalEvent.Region)) },
        });
        date.SetValue(FrameworkElement.StyleProperty, UiKit.Res<Style>("Caption"));
        factory.AppendChild(title);
        factory.AppendChild(date);
        list.ItemTemplate = new DataTemplate { VisualTree = factory };
        var left = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(Framed(list, 380));
        grid.Children.Add(left);
        var editor = new StackPanel();
        Grid.SetColumn(editor, 2);
        grid.Children.Add(editor);

        void Fill(HistoricalEvent? select)
        {
            list.ItemsSource = doc.Data.HistoricalEvents
                .OrderBy(e => GenDate.Parse(e.Date).SortKey ?? long.MaxValue).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            list.SelectedItem = select;
            ShowEditor();
        }
        void ShowEditor()
        {
            editor.Children.Clear();
            if (list.SelectedItem is not HistoricalEvent e)
            {
                editor.Children.Add(Note(doc.Data.HistoricalEvents.Count == 0
                    ? "No events yet.  Add your own, or add the standard list (mostly Britain and Ireland, 1801 to 1969) and edit it."
                    : "Choose an event to edit it.", 0));
                return;
            }
            editor.Children.Add(UiKit.Field(doc, "Title", () => e.Title, v => e.Title = v));
            var dates = new Grid();
            dates.ColumnDefinitions.Add(new ColumnDefinition());
            dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dates.ColumnDefinitions.Add(new ColumnDefinition());
            var from = UiKit.Field(doc, "Date", () => e.Date, v => e.Date = v);
            var to = UiKit.Field(doc, "Until (optional)", () => e.EndDate, v => e.EndDate = v);
            Grid.SetColumn(to, 2);
            dates.Children.Add(from);
            dates.Children.Add(to);
            editor.Children.Add(dates);
            editor.Children.Add(UiKit.Field(doc, "Region", () => e.Region, v => e.Region = v));
            editor.Children.Add(UiKit.Field(doc, "Category", () => e.Category, v => e.Category = v));
            editor.Children.Add(UiKit.Field(doc, "Description", () => e.Description, v => e.Description = v, multiline: true));
            var del = UiKit.Button("GhostButton", "Delete this event", (_, _) =>
            {
                doc.Change(() => doc.Data.HistoricalEvents.Remove(e), structural: false);
                Fill(null);
            }, "Trash");
            del.Foreground = UiKit.Brush("Danger");
            del.HorizontalAlignment = HorizontalAlignment.Left;
            editor.Children.Add(del);
        }
        buttons.Children.Add(UiKit.Button("SecondaryButton", "Add event", (_, _) =>
        {
            var e = new HistoricalEvent { Title = "New event" };
            doc.Change(() => doc.Data.HistoricalEvents.Add(e), structural: false);
            Fill(e);
        }, "Plus"));
        var standard = UiKit.Button("GhostButton", "Add the standard list", (_, _) =>
        {
            var added = 0;
            doc.Change(() =>
            {
                foreach (var e in Timeline.StandardEvents())
                {
                    if (doc.Data.HistoricalEvents.Any(x => string.Equals(x.Title, e.Title, StringComparison.CurrentCultureIgnoreCase) && x.Date == e.Date)) continue;
                    doc.Data.HistoricalEvents.Add(e);
                    added++;
                }
            }, structural: false);
            Fill(null);
        });
        standard.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(standard);
        list.SelectionChanged += (_, _) => ShowEditor();
        Fill(null);
        Run(w, "Historical events", "Shown on each person's timeline when they happen during the person's life.  Saved with the tree.", grid, "Close");
        // Titles and dates edited here change the list order, so other views redraw on close.
        state.Refresh();
    }

    /// <summary>"1845 to 1852  ·  Ireland": an event's dates and region, leaving out the parts it lacks.</summary>
    private sealed class EventLineConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var from = values.ElementAtOrDefault(0) as string ?? "";
            var to = values.ElementAtOrDefault(1) as string ?? "";
            var region = values.ElementAtOrDefault(2) as string ?? "";
            var when = to.Length > 0 ? $"{from} to {to}" : from;
            return string.Join("  ·  ", new[] { when, region }.Where(s => s.Length > 0));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- export options

    /// <summary>File > Export GEDCOM and Person > Export Branch: what to write.  Null when cancelled.</summary>
    public static Gedcom.ExportOptions? ExportOptions(TreeDocument doc, Person? start, bool branch)
    {
        var w = MakeWindow(branch ? "Export branch" : "Export GEDCOM", 560);
        var body = new StackPanel();
        var whole = new RadioButton { Content = "The whole tree", GroupName = "what", IsChecked = !branch, Margin = new Thickness(0, 0, 0, 6) };
        var part = new RadioButton { Content = "A branch", GroupName = "what", IsChecked = branch, Margin = new Thickness(0, 0, 0, 6) };
        body.Children.Add(whole);
        body.Children.Add(part);
        var branchPanel = new StackPanel { Margin = new Thickness(24, 4, 0, 10) };
        var root = start ?? doc.HomePerson;
        var who = new Button { Style = UiKit.Res<Style>("SecondaryButton"), Content = root?.DisplayName ?? "Choose a person...", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var direction = new ComboBox { ItemsSource = new[] { "Ancestors", "Descendants", "Ancestors and descendants" }, SelectedIndex = 2, Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var spouses = new CheckBox { Content = "Include spouses", IsChecked = true };
        var count = UiKit.Text("", "Caption");
        count.Margin = new Thickness(0, 8, 0, 0);
        branchPanel.Children.Add(new TextBlock { Text = "Starting with", Style = UiKit.Res<Style>("FieldLabel") });
        branchPanel.Children.Add(who);
        branchPanel.Children.Add(direction);
        branchPanel.Children.Add(spouses);
        branchPanel.Children.Add(count);
        body.Children.Add(branchPanel);
        body.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });
        var privatise = new CheckBox { Content = "Privatise living people", Margin = new Thickness(0, 0, 0, 8) };
        var includePrivate = new CheckBox { Content = "Include facts and people marked Private" };
        body.Children.Add(privatise);
        body.Children.Add(includePrivate);
        body.Children.Add(Note($"A living person is written with the given name \"{Gedcom.LivingName}\", their surname and sex, and nothing else.  Living means no death recorded, born within the last {Living.BirthYearsLimit} years and married within the last {Living.MarriageYearsLimit}."));

        HashSet<string>? Ids()
        {
            if (whole.IsChecked == true || root is null) return null;
            return TreeTools.Branch(doc, root, direction.SelectedIndex != 1, direction.SelectedIndex != 0, spouses.IsChecked == true);
        }
        void Update()
        {
            branchPanel.IsEnabled = part.IsChecked == true;
            var ids = Ids();
            count.Text = ids is null ? "" : TreeTools.Describe(doc, ids);
        }
        who.Click += (_, _) =>
        {
            var p = PickPerson(doc, "Start the branch with", null);
            if (p is null) return;
            root = p;
            who.Content = p.DisplayName;
            Update();
        };
        whole.Checked += (_, _) => Update();
        part.Checked += (_, _) => Update();
        direction.SelectionChanged += (_, _) => Update();
        spouses.Click += (_, _) => Update();
        Update();

        // A branch needs someone to start from; without one the whole tree would be written.
        bool Validate()
        {
            if (part.IsChecked != true || root is not null) return true;
            MessageBox.Show(w, "Choose the person the branch starts with.", "Export branch", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!Run(w, branch ? "Export branch" : "Export GEDCOM", "Writes GEDCOM 5.5.1, readable by most family tree programs.", body, "Export...", Validate)) return null;
        return new Gedcom.ExportOptions { PersonIds = Ids(), PrivatiseLiving = privatise.IsChecked == true, IncludePrivate = includePrivate.IsChecked == true };
    }

    // ---------------------------------------------------------------- convert names

    /// <summary>Tools > Convert Names: true for "First Middle SURNAME", false for "First Middle Surname", null when cancelled.</summary>
    public static bool? ConvertNames()
    {
        var w = MakeWindow("Convert names", 480);
        var body = new StackPanel();
        var mixed = new RadioButton { Content = "First Middle Surname", GroupName = "case", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var caps = new RadioButton { Content = "First Middle SURNAME", GroupName = "case" };
        body.Children.Add(mixed);
        body.Children.Add(caps);
        body.Children.Add(Note("Every name in the tree is rewritten.  Mc, Mac and O' names keep their inner capital (McDonald, O'Brien), dotted initials stay in capitals, each part of a hyphenated name is converted, and \"of\" and \"or\" in given names stay in lower case.  Undo reverses it."));
        if (!Run(w, "Convert names", "Convert all names to mixed case, or mixed case with the surname in capitals.", body, "Convert")) return null;
        return caps.IsChecked == true;
    }
}
