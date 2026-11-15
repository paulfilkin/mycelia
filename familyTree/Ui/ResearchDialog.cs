using System.Windows;
using System.Windows.Controls;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>What the research question window gives back: the entry's fields, lists one item a line.</summary>
public sealed record ResearchInfo(string Topic, string? FactId, ResearchStatus Status, List<string> Searched, string Result, List<string> Next);

public static partial class Dialogs
{
    /// <summary>
    /// Adds or edits one research question on a person or a couple (the Research tab, TDD 15.2): what it is about,
    /// optionally the fact, where has been searched, what came of it and what to try next, one item a line.
    /// </summary>
    public static ResearchInfo? ResearchQuestion(TreeDocument doc, string ownerName, IReadOnlyList<Fact> facts, ResearchEntry? editing)
    {
        var heading = editing is null ? "Add research question" : "Edit research question";
        var w = MakeWindow(heading, 560);
        var body = new StackPanel();

        var (tp, topic) = Labelled("Question, briefly (such as \"Death, 3 Nov 1871, Hebden Bridge\")", editing?.Topic ?? "");
        body.Children.Add(tp);

        // The fact the question is about, when there is one; choosing it fills an empty question.
        var factRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        factRow.ColumnDefinitions.Add(new ColumnDefinition());
        factRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        factRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        var factStack = new StackPanel();
        factStack.Children.Add(new TextBlock { Text = "About the fact", Style = UiKit.Res<Style>("FieldLabel") });
        var factBox = new ComboBox();
        factBox.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
        foreach (var f in FactOrder.Sorted(facts))
        {
            var label = string.Join(", ", new[] { f.Type.Name, f.Date.Trim(), doc.GetPlace(f.PlaceId)?.ShortName ?? "" }.Where(s => s.Length > 0));
            factBox.Items.Add(new ComboBoxItem { Content = label, Tag = f.Id });
        }
        factBox.SelectedIndex = Math.Max(0, factBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == editing?.FactId && editing?.FactId is not null));
        factBox.SelectionChanged += (_, _) =>
        {
            if (topic.Text.Trim().Length == 0 && factBox.SelectedItem is ComboBoxItem { Tag: string } item) topic.Text = item.Content as string ?? "";
        };
        factStack.Children.Add(factBox);
        factRow.Children.Add(factStack);
        var statusStack = new StackPanel();
        statusStack.Children.Add(new TextBlock { Text = "Status", Style = UiKit.Res<Style>("FieldLabel") });
        var statusBox = new ComboBox();
        foreach (var (s, label) in new[] { (ResearchStatus.Open, "Open"), (ResearchStatus.Found, "Found"), (ResearchStatus.NotFound, "Not found") })
            statusBox.Items.Add(new ComboBoxItem { Content = label, Tag = s });
        statusBox.SelectedIndex = (int)(editing?.Status ?? ResearchStatus.Open);
        statusStack.Children.Add(statusBox);
        Grid.SetColumn(statusStack, 2);
        factRow.Children.Add(statusStack);
        body.Children.Add(factRow);

        TextBox Lines(string label, IEnumerable<string>? lines, double height)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            sp.Children.Add(new TextBlock { Text = label, Style = UiKit.Res<Style>("FieldLabel") });
            var box = new TextBox
            {
                Text = string.Join(Environment.NewLine, lines ?? []),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            };
            sp.Children.Add(box);
            body.Children.Add(sp);
            return box;
        }
        var searched = Lines("Searched, one place a line (such as \"FreeBMD deaths 1870-72 (Ashworth)\")", editing?.Searched, 70);
        var result = Lines("Result", editing is null ? null : [editing.Result], 56);
        var next = Lines("Next steps, one a line", editing?.Next, 56);

        static List<string> Split(string text) =>
            text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        bool Validate()
        {
            if (topic.Text.Trim().Length == 0)
            {
                MessageBox.Show(w, "Say briefly what the question is about, or choose the fact.", heading, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        if (!Run(w, heading, ownerName, body, editing is null ? "Add" : "Save", Validate, topic)) return null;
        return new ResearchInfo(
            topic.Text.Trim(),
            (factBox.SelectedItem as ComboBoxItem)?.Tag as string,
            statusBox.SelectedItem is ComboBoxItem { Tag: ResearchStatus st } ? st : ResearchStatus.Open,
            Split(searched.Text),
            string.Join(" ", Split(result.Text)),
            Split(next.Text));
    }
}
