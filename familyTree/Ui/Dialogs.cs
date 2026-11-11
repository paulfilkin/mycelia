using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui;

public sealed record NewPersonInfo(string Given, string Surname, Sex Sex, string BirthDate, string BirthPlace, string DeathDate, string DeathPlace);

/// <summary>Small dialogs built in code, styled like the rest of the app.</summary>
public static partial class Dialogs
{
    private static Window MakeWindow(string title, double width)
    {
        var owner = Application.Current.MainWindow;
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsVisible: true } ? owner : null,
            ShowInTaskbar = false,
            Background = UiKit.Brush("Surface"),
            FontFamily = UiKit.Res<System.Windows.Media.FontFamily>("UiFont"),
            FontSize = 13,
            Foreground = UiKit.Brush("Ink"),
            UseLayoutRounding = true,
        };
    }

    /// <summary>Wraps content with a heading and OK/Cancel buttons; returns true when OK was chosen.</summary>
    private static bool Run(Window w, string heading, string? subheading, UIElement body, string okText, Func<bool>? validate = null, UIElement? focus = null)
    {
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(UiKit.Text(heading, "H2"));
        if (subheading is not null)
        {
            var sub = UiKit.Text(subheading, "Caption");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(0, 4, 0, 0);
            head.Children.Add(sub);
        }
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = okText, Style = UiKit.Res<Style>("PrimaryButton"), IsDefault = true, MinWidth = 96 };
        var cancel = new Button { Content = "Cancel", Style = UiKit.Res<Style>("SecondaryButton"), IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { if (validate is null || validate()) w.DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        w.Content = root;
        if (focus is not null) w.Loaded += (_, _) => Keyboard.Focus(focus as IInputElement);
        return w.ShowDialog() == true;
    }

    private static (StackPanel Panel, TextBox Box) Labelled(string label, string value = "")
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock { Text = label, Style = UiKit.Res<Style>("FieldLabel") });
        var box = new TextBox { Text = value };
        sp.Children.Add(box);
        return (sp, box);
    }

    private static ComboBox PlaceBox(TreeDocument doc, string value = "")
    {
        return new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemsSource = doc.Data.Places.Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Text = value,
        };
    }

    public static NewPersonInfo? NewPerson(TreeDocument doc, string heading, string? subheading, string surname, Sex sex)
    {
        var w = MakeWindow(heading, 520);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var (gp, given) = Labelled("Given names");
        var (sp, sur) = Labelled("Surname", surname);
        Grid.SetColumn(sp, 2);
        grid.Children.Add(gp);
        grid.Children.Add(sp);

        var sexPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var rbM = new RadioButton { Content = "Male", IsChecked = sex == Sex.Male, Margin = new Thickness(0, 0, 18, 0), GroupName = "sex" };
        var rbF = new RadioButton { Content = "Female", IsChecked = sex == Sex.Female, Margin = new Thickness(0, 0, 18, 0), GroupName = "sex" };
        var rbU = new RadioButton { Content = "Unknown", IsChecked = sex == Sex.Unknown, GroupName = "sex" };
        sexPanel.Children.Add(rbM);
        sexPanel.Children.Add(rbF);
        sexPanel.Children.Add(rbU);
        var sexWrap = new StackPanel();
        sexWrap.Children.Add(new TextBlock { Text = "Sex", Style = UiKit.Res<Style>("FieldLabel") });
        sexWrap.Children.Add(sexPanel);
        Grid.SetRow(sexWrap, 1);
        Grid.SetColumnSpan(sexWrap, 3);
        grid.Children.Add(sexWrap);

        var (bdp, bdate) = Labelled("Birth date");
        var bpp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        bpp.Children.Add(new TextBlock { Text = "Birth place", Style = UiKit.Res<Style>("FieldLabel") });
        var bplace = PlaceBox(doc);
        bpp.Children.Add(bplace);
        Grid.SetRow(bdp, 2);
        Grid.SetRow(bpp, 2);
        Grid.SetColumn(bpp, 2);
        grid.Children.Add(bdp);
        grid.Children.Add(bpp);

        var (ddp, ddate) = Labelled("Death date");
        var dpp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        dpp.Children.Add(new TextBlock { Text = "Death place", Style = UiKit.Res<Style>("FieldLabel") });
        var dplace = PlaceBox(doc);
        dpp.Children.Add(dplace);
        Grid.SetRow(ddp, 3);
        Grid.SetRow(dpp, 3);
        Grid.SetColumn(dpp, 2);
        grid.Children.Add(ddp);
        grid.Children.Add(dpp);

        var hint = UiKit.Text("Dates can be typed freely: \"12 Mar 1890\", \"Abt. 1850\", \"Bet. 1850 and 1855\".", "Caption");
        hint.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(hint, 4);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumnSpan(hint, 3);
        grid.Children.Add(hint);

        bool Validate()
        {
            if (given.Text.Trim().Length == 0 && sur.Text.Trim().Length == 0)
            {
                MessageBox.Show(w, "Enter at least a given name or a surname.", heading, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        if (!Run(w, heading, subheading, grid, "Add person", Validate, given)) return null;
        var s = rbM.IsChecked == true ? Sex.Male : rbF.IsChecked == true ? Sex.Female : Sex.Unknown;
        return new NewPersonInfo(given.Text.Trim(), sur.Text.Trim(), s, bdate.Text.Trim(), bplace.Text.Trim(), ddate.Text.Trim(), dplace.Text.Trim());
    }

    public static Person? PickPerson(TreeDocument doc, string heading, string? subheading, IEnumerable<Person>? exclude = null)
    {
        var w = MakeWindow(heading, 460);
        var skip = exclude?.ToHashSet() ?? [];
        var all = doc.Data.People.Where(p => !skip.Contains(p)).OrderBy(p => p.IndexName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var body = new DockPanel();
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top);
        body.Children.Add(search);
        var list = new ListBox
        {
            Height = 360,
            Style = UiKit.Res<Style>("PlainList"),
            DisplayMemberPath = null,
            ItemTemplate = PersonRowTemplate(),
            ItemsSource = all,
        };
        body.Children.Add(new Border { Child = list, BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) });
        search.TextChanged += (_, _) =>
        {
            var q = search.Text.Trim();
            list.ItemsSource = q.Length == 0 ? all : all.Where(p => Matches(p, q)).ToList();
        };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) w.DialogResult = true; };
        bool Validate() => list.SelectedItem is not null;
        return Run(w, heading, subheading, body, "Choose", Validate, search) ? list.SelectedItem as Person : null;
    }

    public static bool Matches(Person p, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return true;
        return p.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || p.IndexName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || p.LifeSpan.Contains(q, StringComparison.Ordinal);
    }

    public static DataTemplate PersonRowTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Person.IndexName)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        var span = new FrameworkElementFactory(typeof(TextBlock));
        span.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Person.LifeSpan)));
        span.SetValue(FrameworkElement.StyleProperty, UiKit.Res<Style>("Caption"));
        factory.AppendChild(name);
        factory.AppendChild(span);
        return new DataTemplate { VisualTree = factory };
    }

    /// <summary>What the citation window gives back: the source (new or existing) and the citation's details.</summary>
    public sealed record CitationInfo(Source Source, bool IsNew, string Detail, string Url, string Accessed, string Text);

    /// <summary>
    /// Choose a source (or describe a new one) and where in it the fact was found: the page or entry, the record's web
    /// address and when it was looked at, and a transcription or extract (TDD 25).  With <paramref name="existing"/>,
    /// edits that citation (its source stays).
    /// </summary>
    public static CitationInfo? Citation(TreeDocument doc, Citation? existing = null)
    {
        var editing = existing is not null;
        var w = MakeWindow(editing ? "Edit source citation" : "Add source citation", 560);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Source", Style = UiKit.Res<Style>("FieldLabel") });
        var sources = doc.Data.Sources.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var combo = new ComboBox { ItemsSource = sources, Margin = new Thickness(0, 0, 0, 6) };
        if (editing) combo.SelectedItem = doc.GetSource(existing!.SourceId);
        else if (sources.Count > 0) combo.SelectedIndex = 0;
        combo.IsEnabled = !editing;
        body.Children.Add(combo);
        var newCheck = new CheckBox { Content = "Create a new source instead", Margin = new Thickness(0, 4, 0, 10), IsChecked = !editing && sources.Count == 0 };
        if (!editing) body.Children.Add(newCheck);
        var (tp, title) = Labelled("New source title");
        var (ap, author) = Labelled("Author or originator");
        var (up, sourceUrl) = Labelled("Source web address (an online database or catalogue)");
        var newPanel = new StackPanel();
        newPanel.Children.Add(tp);
        newPanel.Children.Add(ap);
        newPanel.Children.Add(up);
        body.Children.Add(newPanel);
        var (dp, detail) = Labelled("Citation detail (page, folio, entry)", existing?.Detail ?? "");
        body.Children.Add(dp);
        var (wp, url) = Labelled("Record's web address", existing?.Url ?? "");
        var (cp, accessed) = Labelled("Accessed (date)", existing?.Accessed ?? "");
        var today = UiKit.Button("GhostButton", "Today", (_, _) => accessed.Text = DateTime.Today.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
        today.Padding = new Thickness(8, 3, 8, 3);
        today.VerticalAlignment = VerticalAlignment.Bottom;
        today.Margin = new Thickness(6, 0, 0, 12);
        var accessedRow = new DockPanel();
        DockPanel.SetDock(today, Dock.Right);
        accessedRow.Children.Add(today);
        accessedRow.Children.Add(cp);
        var webRow = new Grid();
        webRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        webRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        webRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        webRow.Children.Add(wp);
        Grid.SetColumn(accessedRow, 2);
        webRow.Children.Add(accessedRow);
        body.Children.Add(webRow);
        var (xp, text) = Labelled("Text (a transcription or extract of the record)", existing?.Text ?? "");
        text.AcceptsReturn = true;
        text.TextWrapping = TextWrapping.Wrap;
        text.MinHeight = 70;
        text.MaxHeight = 160;
        text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        text.VerticalContentAlignment = VerticalAlignment.Top;
        body.Children.Add(xp);

        void Sync()
        {
            var isNew = newCheck.IsChecked == true;
            newPanel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            combo.IsEnabled = !isNew && !editing;
        }
        newCheck.Checked += (_, _) => Sync();
        newCheck.Unchecked += (_, _) => Sync();
        Sync();

        bool Validate()
        {
            foreach (var box in new[] { url, sourceUrl })
                if (box.Text.Trim().Length > 0 && !IsWebAddress(box.Text.Trim()))
                {
                    MessageBox.Show(w, "A web address starts with https:// or http://.", w.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            if (newCheck.IsChecked == true ? title.Text.Trim().Length > 0 : combo.SelectedItem is not null) return true;
            MessageBox.Show(w, "Choose a source or give the new one a title.", w.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!Run(w, w.Title, editing ? "Where in the source, and the record's own details." : "Link this fact to the record it came from.", body, editing ? "Save" : "Add", Validate, detail)) return null;
        var isNew = newCheck.IsChecked == true;
        var source = isNew
            ? new Source { Title = title.Text.Trim(), Author = author.Text.Trim(), Url = sourceUrl.Text.Trim() }
            : (Source)combo.SelectedItem!;
        return new CitationInfo(source, isNew, detail.Text.Trim(), url.Text.Trim(), accessed.Text.Trim(), text.Text.Trim());
    }

    /// <summary>An http or https address (the only ones the program opens).</summary>
    public static bool IsWebAddress(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>Opens an http or https address in the browser.</summary>
    public static void OpenWebAddress(string text)
    {
        if (!IsWebAddress(text)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(text) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no browser */ }
    }

    /// <summary>A clickable web address, shortened to fit, for a citation or source.</summary>
    public static FrameworkElement WebLink(string url, double fontSize = 12)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(url)) { Foreground = UiKit.Brush("Brand") };
        link.Click += (_, e) => { OpenWebAddress(url); e.Handled = true; };
        return new TextBlock(link) { FontSize = fontSize, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = url };
    }

    public static T? PickItem<T>(string heading, string? subheading, IReadOnlyList<T> items, Func<T, string> display) where T : class
    {
        var w = MakeWindow(heading, 460);
        var body = new DockPanel();
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top);
        body.Children.Add(search);
        var list = new ListBox { Height = 320, Style = UiKit.Res<Style>("PlainList") };
        void Fill()
        {
            var q = search.Text.Trim();
            list.Items.Clear();
            foreach (var item in items.Where(i => q.Length == 0 || display(i).Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                list.Items.Add(new ListBoxItem { Content = display(item), Tag = item, Style = UiKit.Res<Style>("ListRow") });
        }
        Fill();
        search.TextChanged += (_, _) => Fill();
        body.Children.Add(new Border { Child = list, BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) });
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) w.DialogResult = true; };
        return Run(w, heading, subheading, body, "Choose", () => list.SelectedItem is not null, search)
            ? (list.SelectedItem as ListBoxItem)?.Tag as T
            : null;
    }

    /// <summary>
    /// Merge two people: the two records side by side, the left one kept.  Returns the pair to merge
    /// (kept, merged into it), or null when cancelled.
    /// </summary>
    public static (Person Keep, Person Merge)? MergePeople(TreeDocument doc, Person? left, Person? right)
    {
        var w = MakeWindow("Merge two people", 760);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var leftPanel = new StackPanel();
        var rightPanel = new StackPanel();
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(leftPanel);
        grid.Children.Add(rightPanel);

        void Fill(StackPanel panel, Person? p, string heading, bool isLeft)
        {
            panel.Children.Clear();
            panel.Children.Add(UiKit.Text(heading, "SectionLabel"));
            var choose = new Button
            {
                Style = UiKit.Res<Style>("SecondaryButton"),
                Content = p?.DisplayName ?? "Choose a person...",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
            };
            choose.Click += (_, _) =>
            {
                var other = isLeft ? right : left;
                var picked = PickPerson(doc, "Choose a person", null, other is null ? null : [other]);
                if (picked is null) return;
                if (isLeft) left = picked; else right = picked;
                Refresh();
            };
            panel.Children.Add(choose);
            if (p is null) return;
            var card = new StackPanel();
            void Line(string label, string value)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(UiKit.Text(label, "Caption"));
                var v = UiKit.Text(value.Length == 0 ? "-" : value, size: 12.5);
                v.TextWrapping = TextWrapping.Wrap;
                v.TextTrimming = TextTrimming.None;
                Grid.SetColumn(v, 1);
                g.Children.Add(v);
                card.Children.Add(g);
            }
            string When(Fact? f) => f is null ? "" : string.Join(", ", new[] { f.ParsedDate.Display, doc.PlaceName(f.PlaceId) }.Where(s => s.Length > 0));
            Line("Sex", p.Sex.ToString());
            Line("Born", When(p.Birth));
            Line("Died", When(p.Death));
            Line("Parents", string.Join(", ", doc.Parents(p).Select(x => x.DisplayName)));
            Line("Spouses", string.Join(", ", doc.Spouses(p).Select(x => x.DisplayName)));
            Line("Children", doc.Children(p).Count().ToString());
            Line("Facts", $"{p.Facts.Count}, with {p.Facts.Sum(f => f.Citations.Count)} citations");
            panel.Children.Add(new Border { Style = UiKit.Res<Style>("Card"), Padding = new Thickness(12), Background = UiKit.Brush(isLeft ? "BrandSofter" : "SurfaceAlt"), Child = card });
        }

        var swap = UiKit.IconButton("Redo", "Swap: keep the other record", (_, _) => { (left, right) = (right, left); Refresh(); });
        swap.VerticalAlignment = VerticalAlignment.Center;
        swap.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(swap, 1);
        grid.Children.Add(swap);

        void Refresh()
        {
            Fill(leftPanel, left, "KEEP THIS RECORD", true);
            Fill(rightPanel, right, "MERGE INTO IT, THEN REMOVE", false);
        }
        Refresh();

        var body = new StackPanel();
        body.Children.Add(grid);
        var note = UiKit.Text("Facts, sources, relatives, notes and media from the right are added to the left.  Matching facts are combined, a different name is kept as \"Also Known As\", and gaps on the left are filled from the right.  Undo reverses the whole merge.", "Caption");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(note);

        bool Validate()
        {
            if (left is not null && right is not null && left != right) return true;
            MessageBox.Show(w, "Choose two different people.", "Merge two people", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!Run(w, "Merge two people", "Use this when the same person has been entered twice.", body, "Merge", Validate)) return null;
        return (left!, right!);
    }

    public static string? Prompt(string heading, string label, string initial)
    {
        var w = MakeWindow(heading, 440);
        var (panel, box) = Labelled(label, initial);
        box.SelectAll();
        return Run(w, heading, null, panel, "OK", () => box.Text.Trim().Length > 0, box) ? box.Text.Trim() : null;
    }

    public static bool Confirm(string heading, string message, string action)
    {
        var w = MakeWindow(heading, 440);
        var t = UiKit.Text(message);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return Run(w, heading, null, t, action);
    }
}
