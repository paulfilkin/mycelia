using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>Relationship, date and Soundex calculators, and the data-error check.</summary>
public sealed class ToolsView : UserControl, IWorkspace
{
    private AppState? _state;
    private Person? _relA;
    private Person? _relB;
    private readonly StackPanel _relResult = new();
    private readonly Button _relAButton;
    private readonly Button _relBButton;
    private readonly StackPanel _errors = new();
    private readonly TextBlock _errorSummary = UiKit.Text("", "Caption");

    public ToolsView()
    {
        _relAButton = PersonButton(() => _relA, p => { _relA = p; Calculate(); });
        _relBButton = PersonButton(() => _relB, p => { _relB = p; Calculate(); });

        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 28), MaxWidth = 1240 };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

        top.Children.Add(UiKit.Card(RelationshipCard()));
        var date = UiKit.Card(DateCard());
        Grid.SetColumn(date, 2);
        top.Children.Add(date);
        var soundex = UiKit.Card(SoundexCard());
        Grid.SetColumn(soundex, 4);
        top.Children.Add(soundex);
        page.Children.Add(top);

        _duplicatesCard = UiKit.Card(DuplicatesCard());
        _duplicatesCard.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(_duplicatesCard);

        var errors = UiKit.Card(ErrorsCard());
        errors.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(errors);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public void Attach(AppState state)
    {
        _state = state;
        state.DocumentReplaced += () => { _relA = _relB = null; _errors.Children.Clear(); _errorSummary.Text = ""; };
    }

    public void OnShown()
    {
        if (_state is null) return;
        var doc = _state.Doc;
        _relA = doc.GetPerson(_relA?.Id) ?? doc.HomePerson;
        _relB = doc.GetPerson(_relB?.Id) ?? _state.Selected;
        if (_relA == _relB) _relB = doc.Data.People.FirstOrDefault(p => p != _relA);
        Calculate();
    }

    private static FrameworkElement CardTitle(string icon, string title, string about)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = UiKit.Icon(icon, 20, UiKit.Brush("Brand"));
        ic.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(ic);
        row.Children.Add(UiKit.Text(title, "H2"));
        sp.Children.Add(row);
        var a = UiKit.Text(about, "Caption");
        a.TextWrapping = TextWrapping.Wrap;
        a.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(a);
        return sp;
    }

    // ---------------------------------------------------------------- relationship calculator

    private Button PersonButton(Func<Person?> get, Action<Person> set)
    {
        var b = new Button { Style = UiKit.Res<Style>("SecondaryButton"), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) =>
        {
            var p = Dialogs.PickPerson(_state!.Doc, "Choose a person", null);
            if (p is not null) set(p);
        };
        return b;
    }

    private FrameworkElement RelationshipCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(CardTitle("People", "Relationship calculator", "How two people in the tree are related, and the line that joins them."));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var a = new StackPanel();
        a.Children.Add(new TextBlock { Text = "Person", Style = UiKit.Res<Style>("FieldLabel") });
        a.Children.Add(_relAButton);
        g.Children.Add(a);
        var swap = UiKit.IconButton("Redo", "Swap", (_, _) => { (_relA, _relB) = (_relB, _relA); Calculate(); });
        swap.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(swap, 1);
        g.Children.Add(swap);
        var b = new StackPanel();
        b.Children.Add(new TextBlock { Text = "is related to", Style = UiKit.Res<Style>("FieldLabel") });
        b.Children.Add(_relBButton);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        sp.Children.Add(g);
        _relResult.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(_relResult);
        return sp;
    }

    private void Calculate()
    {
        _relAButton.Content = _relA?.DisplayName ?? "Choose...";
        _relBButton.Content = _relB?.DisplayName ?? "Choose...";
        _relResult.Children.Clear();
        if (_state is null || _relA is null || _relB is null) return;
        var result = Kinship.Describe(_state.Doc, _relA, _relB);
        var headline = new Border { Background = UiKit.Brush("BrandSoft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12) };
        var hs = new StackPanel();
        if (result.Text is "unrelated" or "self")
        {
            hs.Children.Add(UiKit.Text(result.Text == "self" ? "The same person" : "Not related", "H2", brush: UiKit.Brush("Brand")));
            hs.Children.Add(UiKit.Text(result.Text == "self" ? "Choose two different people." : "No line of parents, children or spouses joins them in this tree.", size: 13, brush: UiKit.Brush("Muted")));
        }
        else
        {
            hs.Children.Add(UiKit.Text($"{_relB.DisplayName} is {Article(result.Text)}", size: 13, brush: UiKit.Brush("Muted")));
            hs.Children.Add(UiKit.Text(result.Text, "H2", brush: UiKit.Brush("Brand")));
            hs.Children.Add(UiKit.Text($"of {_relA.DisplayName}", size: 13, brush: UiKit.Brush("Muted")));
        }
        headline.Child = hs;
        _relResult.Children.Add(headline);
        if (result.Path.Count > 1)
        {
            _relResult.Children.Add(UiKit.Text("PATH", "SectionLabel"));
            foreach (var step in result.Path)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
                var rel = UiKit.Text(step.Relation, "Caption");
                DockPanel.SetDock(rel, Dock.Right);
                row.Children.Add(rel);
                var av = UiKit.Avatar(_state.Doc, step.Person, 24);
                row.Children.Add(av);
                var name = UiKit.Text(step.Person.DisplayName, weight: step.Person == _relA || step.Person == _relB ? FontWeights.SemiBold : FontWeights.Normal);
                name.Margin = new Thickness(8, 0, 0, 0);
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                var person = step.Person;
                row.MouseLeftButtonUp += (_, _) => { _state.Select(person, focus: true); _state.Navigate("People"); };
                _relResult.Children.Add(row);
            }
        }
    }

    private static string Article(string relation) =>
        relation == "unrelated" || relation == "self" ? "" : relation.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(relation[0])) ? "an" : "the";

    // ---------------------------------------------------------------- date calculator

    private FrameworkElement DateCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(CardTitle("Calendar", "Date calculator", "Work out an age, a birth date from an age, or an event date."));

        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var mAge = new RadioButton { Content = "Age", IsChecked = true, GroupName = "dc", Margin = new Thickness(0, 0, 16, 0) };
        var mBirth = new RadioButton { Content = "Birth date", GroupName = "dc", Margin = new Thickness(0, 0, 16, 0) };
        var mEvent = new RadioButton { Content = "Event date", GroupName = "dc" };
        modes.Children.Add(mAge);
        modes.Children.Add(mBirth);
        modes.Children.Add(mEvent);
        sp.Children.Add(modes);

        var firstLabel = new TextBlock { Style = UiKit.Res<Style>("FieldLabel") };
        var first = new TextBox { Text = "14 Aug 1884", Margin = new Thickness(0, 0, 0, 12) };
        var secondLabel = new TextBlock { Style = UiKit.Res<Style>("FieldLabel") };
        var second = new TextBox { Text = "29 Mar 1962", Margin = new Thickness(0, 0, 0, 12) };
        var ageRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < 5; i++) ageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(10) : new GridLength(1, GridUnitType.Star) });
        TextBox AgeBox(string label, int col)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = label, Style = UiKit.Res<Style>("FieldLabel") });
            var box = new TextBox { Text = "0" };
            p.Children.Add(box);
            Grid.SetColumn(p, col);
            ageRow.Children.Add(p);
            return box;
        }
        var years = AgeBox("Years", 0);
        var months = AgeBox("Months", 2);
        var days = AgeBox("Days", 4);

        sp.Children.Add(firstLabel);
        sp.Children.Add(first);
        sp.Children.Add(secondLabel);
        sp.Children.Add(second);
        sp.Children.Add(ageRow);
        var result = new Border { Background = UiKit.Brush("BrandSoft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10) };
        var resultText = UiKit.Text("", "H3", brush: UiKit.Brush("Brand"));
        resultText.TextWrapping = TextWrapping.Wrap;
        var resultSub = UiKit.Text("", "Caption");
        var rs = new StackPanel();
        rs.Children.Add(resultText);
        rs.Children.Add(resultSub);
        result.Child = rs;
        sp.Children.Add(result);

        static (int Y, int M, int D)? Full(string text)
        {
            var d = GenDate.Parse(text);
            if (!d.IsValid || d.IsRange || d.Start.Year is null) return null;
            return (d.Start.Year.Value, d.Start.Month ?? 1, d.Start.Day ?? 1);
        }
        static string Show((int Y, int M, int D) d) => GenDate.Parse($"{d.D} {new DateTime(2000, d.M, 1):MMM} {(d.Y < 0 ? $"{-d.Y} BC" : d.Y.ToString())}").Display;
        static string Weekday((int Y, int M, int D) d) => GenDate.WeekdayName(GenDate.ToDayNumber(d.Y, d.M, d.D));
        static int Num(TextBox b) => int.TryParse(b.Text.Trim(), out var n) ? Math.Max(0, n) : 0;

        void Update()
        {
            var mode = mAge.IsChecked == true ? 0 : mBirth.IsChecked == true ? 1 : 2;
            firstLabel.Text = mode switch { 0 => "Birth date", 1 => "Event date", _ => "Birth date" };
            secondLabel.Text = "Event date";
            secondLabel.Visibility = second.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            ageRow.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
            var a = Full(first.Text);
            if (a is null) { resultText.Text = "Enter a full date"; resultSub.Text = "For example 14 Aug 1884."; return; }
            switch (mode)
            {
                case 0:
                {
                    var b = Full(second.Text);
                    if (b is null) { resultText.Text = "Enter a second date"; resultSub.Text = ""; return; }
                    var age = Age.Between(a.Value.Y, a.Value.M, a.Value.D, b.Value.Y, b.Value.M, b.Value.D);
                    resultText.Text = age.ToString();
                    resultSub.Text = $"{Show(a.Value)} was a {Weekday(a.Value)};  {Show(b.Value)} was a {Weekday(b.Value)}.";
                    break;
                }
                case 1:
                {
                    var r = Age.Subtract(a.Value.Y, a.Value.M, a.Value.D, Num(years), Num(months), Num(days));
                    resultText.Text = $"Born {Show(r)}";
                    resultSub.Text = $"A {Weekday(r)}.  Record it as \"Cal. {Show(r)}\" when it comes from an age.";
                    break;
                }
                default:
                {
                    var r = Age.Add(a.Value.Y, a.Value.M, a.Value.D, Num(years), Num(months), Num(days));
                    resultText.Text = $"Event on {Show(r)}";
                    resultSub.Text = $"A {Weekday(r)}.";
                    break;
                }
            }
        }
        foreach (var rb in new[] { mAge, mBirth, mEvent }) rb.Checked += (_, _) => Update();
        foreach (var tb in new[] { first, second, years, months, days }) tb.TextChanged += (_, _) => Update();
        Update();
        return sp;
    }

    // ---------------------------------------------------------------- soundex

    private FrameworkElement SoundexCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(CardTitle("Search", "Soundex", "Surnames that sound alike share a code, which helps with spelling variants in old records."));
        sp.Children.Add(new TextBlock { Text = "Surname", Style = UiKit.Res<Style>("FieldLabel") });
        var box = new TextBox { Text = "Ashworth", Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(box);
        var code = UiKit.Text("", "H1", brush: UiKit.Brush("Brand"));
        sp.Children.Add(code);
        var matches = UiKit.Text("", "Caption");
        matches.TextWrapping = TextWrapping.Wrap;
        matches.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(matches);
        void Update()
        {
            var c = Soundex.Code(box.Text);
            code.Text = c.Length == 0 ? "-" : c;
            if (_state is null || c.Length == 0) { matches.Text = ""; return; }
            var same = _state.Doc.Data.People.Select(p => p.Surname).Where(s => s.Length > 0 && Soundex.Code(s) == c)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s).ToList();
            matches.Text = same.Count == 0 ? "No surnames in this tree share the code." : "In this tree: " + string.Join(", ", same);
        }
        box.TextChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
        return sp;
    }

    // ---------------------------------------------------------------- data errors

    private FrameworkElement ErrorsCard()
    {
        var sp = new StackPanel();
        var head = new DockPanel();
        var run = UiKit.Button("PrimaryButton", "Check the tree", (_, _) => RunCheck(), "Check");
        run.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(run, Dock.Right);
        head.Children.Add(run);
        head.Children.Add(CardTitle("Warning", "Find data errors",
            $"Looks for improbable facts: births after deaths, parents younger than {DataChecker.ParentMinimumAge} or older than {DataChecker.MotherMaximumAge} (mothers) and {DataChecker.FatherMaximumAge} (fathers), ages over {DataChecker.QuestionableDeathAge}, and missing details."));
        sp.Children.Add(head);
        sp.Children.Add(_errorSummary);
        _errors.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(_errors);
        return sp;
    }

    // ---------------------------------------------------------------- duplicate people

    private Border? _duplicatesCard;
    private readonly StackPanel _duplicates = new();
    private readonly TextBlock _duplicateSummary = UiKit.Text("", "Caption");

    private FrameworkElement DuplicatesCard()
    {
        var sp = new StackPanel();
        var head = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(UiKit.Button("SecondaryButton", "Merge two people...", (_, _) => PersonActions.Merge(_state!, _state!.Selected, null), "Link"));
        var find = UiKit.Button("PrimaryButton", "Find duplicates", (_, _) => RunDuplicates(), "Search");
        find.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(find);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(CardTitle("People", "Duplicate people",
            $"Finds people who may have been entered twice: surnames that sound alike, matching given names, and births within {PersonMerge.BirthYearTolerance} years.  Compare each pair and merge the ones that are the same person."));
        sp.Children.Add(head);
        sp.Children.Add(_duplicateSummary);
        _duplicates.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(_duplicates);
        return sp;
    }

    /// <summary>Runs the duplicate search and brings its card into view (main menu, Find duplicate people).</summary>
    public void ShowDuplicates()
    {
        RunDuplicates();
        _duplicatesCard?.BringIntoView();
    }

    private void RunDuplicates()
    {
        if (_state is null) return;
        _duplicates.Children.Clear();
        var pairs = PersonMerge.FindDuplicates(_state.Doc);
        _duplicateSummary.Text = pairs.Count == 0
            ? "No likely duplicates found."
            : pairs.Count >= PersonMerge.MaxCandidates ? $"The first {pairs.Count} likely pairs, closest first." : $"{pairs.Count} likely pairs, closest first.";
        foreach (var pair in pairs)
        {
            var row = new Grid { Margin = new Thickness(-8, 0, -8, 2), Background = System.Windows.Media.Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement Who(Person p)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
                sp.Children.Add(UiKit.Avatar(_state.Doc, p, 26));
                var t = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(UiKit.Text(p.DisplayName, weight: FontWeights.Medium));
                t.Children.Add(UiKit.Text(p.LifeSpan.Length > 0 ? p.LifeSpan : "No dates", "Caption"));
                sp.Children.Add(t);
                return sp;
            }
            row.Children.Add(Who(pair.A));
            var b = Who(pair.B);
            Grid.SetColumn(b, 1);
            row.Children.Add(b);
            var reason = UiKit.Text(pair.Reason, "Caption");
            reason.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(reason, 2);
            row.Children.Add(reason);
            var compare = UiKit.Button("GhostButton", "Compare and merge...", (_, _) =>
            {
                PersonActions.Merge(_state, pair.A, pair.B);
                RunDuplicates();
            }, "Link");
            compare.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(compare, 3);
            row.Children.Add(compare);
            row.MouseEnter += (_, _) => row.Background = UiKit.Brush("Hover");
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
            _duplicates.Children.Add(row);
        }
    }

    private void RunCheck()
    {
        if (_state is null) return;
        _errors.Children.Clear();
        var issues = DataChecker.Check(_state.Doc);
        var serious = issues.Count(i => i.Category != "Missing");
        _errorSummary.Text = issues.Count == 0
            ? "No problems found."
            : $"{serious} possible errors and {issues.Count - serious} missing details, for {issues.Select(i => i.Person).Distinct().Count()} people.";
        foreach (var group in issues.OrderBy(i => i.Category == "Missing").GroupBy(i => i.Person).Take(300))
        {
            var row = new Grid { Margin = new Thickness(-8, 0, -8, 2), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Top };
            who.Children.Add(UiKit.Avatar(_state.Doc, group.Key, 26));
            var name = UiKit.Text(group.Key.DisplayName, weight: FontWeights.Medium);
            name.Margin = new Thickness(8, 0, 0, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(name);
            row.Children.Add(who);
            var msgs = new StackPanel { Margin = new Thickness(0, 6, 8, 6) };
            foreach (var issue in group)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                var missing = issue.Category == "Missing";
                var ic = UiKit.Icon(missing ? "Note" : "Warning", 14, UiKit.Brush(missing ? "Subtle" : "Warning"));
                ic.Margin = new Thickness(0, 0, 8, 0);
                line.Children.Add(ic);
                line.Children.Add(UiKit.Text(issue.Message, brush: UiKit.Brush(missing ? "Muted" : "Ink")));
                msgs.Children.Add(line);
            }
            Grid.SetColumn(msgs, 1);
            row.Children.Add(msgs);
            var person = group.Key;
            row.MouseEnter += (_, _) => row.Background = UiKit.Brush("Hover");
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => { _state.Select(person, focus: true); _state.Navigate("People"); };
            _errors.Children.Add(row);
        }
    }
}
