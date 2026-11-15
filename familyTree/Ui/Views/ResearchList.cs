using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>
/// A person's or a couple's research log as the person panel shows it (TDD 15.2): each question with its status and
/// date, where has been searched, the result and the next steps, and buttons to edit it, change its status or delete
/// it; Add research question below.  Every change is one undo step, and finds the owner and entry again by id, since
/// undo replaces every object.
/// </summary>
public static class ResearchList
{
    public static FrameworkElement Build(AppState state, object owner, string ownerName)
    {
        // Normal weight here: inside the person panel's tabs, text otherwise takes the selected tab's heading weight.
        var sp = new StackPanel();
        TextElement.SetFontWeight(sp, FontWeights.Normal);
        var ownerId = owner switch { Person p => p.Id, Family f => f.Id, _ => "" };
        // Open questions first, then the most recently changed.
        var entries = ResearchLog.Of(owner).OrderBy(e => e.Status != ResearchStatus.Open).ThenByDescending(e => e.Updated).ToList();
        if (entries.Count == 0)
        {
            var none = UiKit.Text("No research questions yet.  Each one records where has been searched, what came of it and what to try next.", "Caption");
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(none);
        }
        foreach (var e in entries) sp.Children.Add(Entry(state, ownerId, ownerName, e));
        var add = UiKit.Button("GhostButton", "Add research question", (_, _) => AddQuestion(state, ownerId, ownerName), "Plus");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(add);
        return sp;
    }

    /// <summary>The number of open questions, for the tab's header ("Research (2)").</summary>
    public static int OpenCount(object owner) => ResearchLog.Of(owner).Count(e => e.Status == ResearchStatus.Open);

    public static string StatusText(ResearchStatus s) => s switch { ResearchStatus.Found => "Found", ResearchStatus.NotFound => "Not found", _ => "Open" };

    private static FrameworkElement Entry(AppState state, string ownerId, string ownerName, ResearchEntry e)
    {
        var card = new Border
        {
            Background = UiKit.Brush("SurfaceAlt"), BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 8), Margin = new Thickness(0, 0, 0, 10),
        };
        var sp = new StackPanel();

        // The question, with its status and the day it last changed.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var chip = new Border { Style = UiKit.Res<Style>("Badge"), Margin = new Thickness(8, 0, 6, 0) };
        if (e.Status == ResearchStatus.NotFound) chip.Background = UiKit.Brush("Border");
        if (e.Status == ResearchStatus.Open) chip.Background = UiKit.Brush("WarningSoft");
        chip.Child = UiKit.Text(StatusText(e.Status), size: 11.5, brush: UiKit.Brush(e.Status == ResearchStatus.Found ? "Brand" : "Ink"), weight: FontWeights.Medium);
        meta.Children.Add(chip);
        var date = UiKit.Text(e.Updated.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), "Caption");
        date.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(date);
        DockPanel.SetDock(meta, Dock.Right);
        head.Children.Add(meta);
        var topic = UiKit.Text(e.Topic, weight: FontWeights.SemiBold);
        topic.TextWrapping = TextWrapping.Wrap;
        topic.TextTrimming = TextTrimming.None;
        head.Children.Add(topic);
        sp.Children.Add(head);

        void Line(string label, string text)
        {
            if (text.Length == 0) return;
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = UiKit.Brush("Ink") };
            t.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = UiKit.Brush("Muted") });
            t.Inlines.Add(new System.Windows.Documents.Run(text));
            sp.Children.Add(t);
        }
        Line("Searched: ", string.Join("; ", e.Searched));
        Line("", e.Result);
        foreach (var n in e.Next) Line("Next: ", n);

        // Change it: edit, set the status, delete.
        var actions = new WrapPanel { Margin = new Thickness(-8, 4, 0, 0) };
        var id = e.Id;
        actions.Children.Add(UiKit.Button("GhostButton", "Edit...", (_, _) => Edit(state, ownerId, ownerName, id), "Edit"));
        if (e.Status == ResearchStatus.Open)
        {
            actions.Children.Add(UiKit.Button("GhostButton", "Mark found", (_, _) => SetStatus(state, ownerId, id, ResearchStatus.Found), "Check"));
            actions.Children.Add(UiKit.Button("GhostButton", "Mark not found", (_, _) => SetStatus(state, ownerId, id, ResearchStatus.NotFound), "Close"));
        }
        else actions.Children.Add(UiKit.Button("GhostButton", "Reopen", (_, _) => SetStatus(state, ownerId, id, ResearchStatus.Open), "Undo"));
        var delete = UiKit.Button("GhostButton", "Delete", (_, _) => Delete(state, ownerId, id, e.Topic), "Trash");
        delete.Foreground = UiKit.Brush("Muted");
        actions.Children.Add(delete);
        sp.Children.Add(actions);

        card.Child = sp;
        return card;
    }

    private static object? Owner(TreeDocument doc, string ownerId) => (object?)doc.GetPerson(ownerId) ?? doc.GetFamily(ownerId);

    private static IReadOnlyList<Fact> FactsOf(object owner) => owner switch { Person p => p.Facts, Family f => f.Facts, _ => [] };

    private static ResearchEntry? FindEntry(TreeDocument doc, string ownerId, string entryId) =>
        Owner(doc, ownerId) is { } owner ? ResearchLog.Of(owner).FirstOrDefault(x => x.Id == entryId) : null;

    /// <summary>Asks for a new research question and records it (a family card's Research question... uses it too).</summary>
    public static void AddQuestion(AppState state, string ownerId, string ownerName)
    {
        if (Owner(state.Doc, ownerId) is not { } owner) return;
        if (Dialogs.ResearchQuestion(state.Doc, ownerName, FactsOf(owner), null) is not { } info) return;
        state.Doc.Change(() =>
        {
            // The same question already logged is updated rather than added twice (ResearchLog.Record).
            if (Owner(state.Doc, ownerId) is { } o)
                ResearchLog.Record(o, info.FactId, info.Topic, info.Status, info.Searched, info.Result, info.Next, DateTime.Today);
        });
    }

    private static void Edit(AppState state, string ownerId, string ownerName, string entryId)
    {
        if (Owner(state.Doc, ownerId) is not { } owner || FindEntry(state.Doc, ownerId, entryId) is not { } entry) return;
        if (Dialogs.ResearchQuestion(state.Doc, ownerName, FactsOf(owner), entry) is not { } info) return;
        state.Doc.Change(() =>
        {
            if (FindEntry(state.Doc, ownerId, entryId) is not { } e) return;
            e.Topic = info.Topic;
            e.FactId = info.FactId;
            e.Status = info.Status;
            e.Searched = info.Searched;
            e.Result = info.Result;
            e.Next = info.Next;
            e.Updated = DateTime.Today;
        });
    }

    private static void SetStatus(AppState state, string ownerId, string entryId, ResearchStatus status) =>
        state.Doc.Change(() =>
        {
            if (FindEntry(state.Doc, ownerId, entryId) is not { } e) return;
            e.Status = status;
            e.Updated = DateTime.Today;
        });

    private static void Delete(AppState state, string ownerId, string entryId, string topic)
    {
        if (!Dialogs.Confirm("Delete research question", $"Delete \"{topic}\" from the research log?  Undo brings it back.", "Delete")) return;
        state.Doc.Change(() =>
        {
            if (Owner(state.Doc, ownerId) is not { } owner) return;
            switch (owner)
            {
                case Person p: p.Research?.RemoveAll(x => x.Id == entryId); break;
                case Family f: f.Research?.RemoveAll(x => x.Id == entryId); break;
            }
            ResearchLog.Tidy(owner);
        });
    }
}
