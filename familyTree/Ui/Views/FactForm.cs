using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>
/// The form for editing one fact (type, date, place, description, note, preferred, private, sources),
/// shared by the person panel beside the tree and the Person view.
/// </summary>
public static class FactForm
{
    /// <summary>Fact types offered first in the "Add fact" menu.</summary>
    public static readonly int[] CommonFacts =
        [FactTypes.Birth, FactTypes.Baptism, FactTypes.Christening, FactTypes.Death, FactTypes.Burial, FactTypes.Residence,
         FactTypes.Occupation, 15, 23, FactTypes.MilitaryService, 33, FactTypes.Emigration, FactTypes.AlsoKnownAs];

    /// <summary>"Place  ·  description" as shown in fact lists.</summary>
    public static string Detail(TreeDocument doc, Fact f) =>
        string.Join("  ·  ", new[] { doc.PlaceName(f.PlaceId), f.Description }.Where(s => s.Length > 0));

    /// <summary>Opens the menu of fact types under <paramref name="anchor"/>; <paramref name="created"/> gets the new fact.</summary>
    public static void ShowAddMenu(TreeDocument doc, Button anchor, List<Fact> facts, FactScope scope, Person? person, Action<Fact> created)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void AddItem(ItemsControl parent, FactType t)
        {
            var mi = new MenuItem { Header = t.Name };
            mi.Click += (_, _) =>
            {
                Fact? fact = null;
                doc.Change(() => fact = doc.AddFact(facts, t.Id, "", ""), structural: false);
                person?.RaiseFactsChanged();
                if (fact is not null) created(fact);
            };
            parent.Items.Add(mi);
        }
        if (scope == FactScope.Person)
        {
            foreach (var id in CommonFacts) AddItem(menu, FactTypes.Get(id));
            var more = new MenuItem { Header = "More facts" };
            foreach (var t in FactTypes.PersonTypes.Where(t => !CommonFacts.Contains(t.Id))) AddItem(more, t);
            menu.Items.Add(new Separator());
            menu.Items.Add(more);
        }
        else
        {
            foreach (var t in FactTypes.CoupleTypes) AddItem(menu, t);
        }
        menu.IsOpen = true;
    }

    /// <summary>
    /// Builds the form.  <paramref name="changed"/> runs after any edit that alters what a fact list shows
    /// (type, date, place, description, flags); <paramref name="rebuild"/> after sources are added or removed
    /// or the fact is deleted, when the caller should build its view again.
    /// </summary>
    public static FrameworkElement Build(TreeDocument doc, Fact f, List<Fact> facts, Person? person, Action changed, Action rebuild, Action deleted)
    {
        var sp = new StackPanel();
        var scope = f.Type.Scope;

        sp.Children.Add(new TextBlock { Text = "Fact", Style = UiKit.Res<Style>("FieldLabel") });
        var types = scope == FactScope.Person ? FactTypes.PersonTypes : FactTypes.CoupleTypes;
        var typeBox = new ComboBox { ItemsSource = types, SelectedItem = types.FirstOrDefault(t => t.Id == f.TypeId), Margin = new Thickness(0, 0, 0, 12) };
        typeBox.SelectionChanged += (_, _) =>
        {
            if (typeBox.SelectedItem is FactType t && t.Id != f.TypeId)
            {
                doc.Change(() => f.TypeId = t.Id, structural: false);
                person?.RaiseFactsChanged();
                changed();
            }
        };
        sp.Children.Add(typeBox);

        var hint = UiKit.Text("", "Caption");
        hint.TextWrapping = TextWrapping.Wrap;
        void UpdateHint()
        {
            var d = f.ParsedDate;
            if (d.IsEmpty) { hint.Text = "Type a date such as 12 Mar 1890, Abt. 1850, Bef. 1900 or Bet. 1850 and 1855."; hint.Foreground = UiKit.Brush("Subtle"); }
            else if (!d.IsValid) { hint.Text = "Not a recognised date. It will be kept as typed."; hint.Foreground = UiKit.Brush("Warning"); }
            else
            {
                var text = $"Reads as {d.Display}";
                if (person?.Birth is { } b && b != f && b.ParsedDate.IsValid && Age.YearsBetween(b.ParsedDate.Start, d.Start) is int age && d.Start.DayNumber >= b.ParsedDate.Start.DayNumber)
                    text += $"   ·   age {age}";
                hint.Text = text;
                hint.Foreground = UiKit.Brush("Muted");
            }
        }
        var dateField = UiKit.Field(doc, "Date", () => f.Date, v => { f.Date = v; UpdateHint(); }, afterChange: () => { person?.RaiseFactsChanged(); changed(); });
        ((Panel)dateField).Margin = new Thickness(0, 0, 0, 4);
        sp.Children.Add(dateField);
        hint.Margin = new Thickness(0, 0, 0, 12);
        sp.Children.Add(hint);
        UpdateHint();

        sp.Children.Add(new TextBlock { Text = "Place", Style = UiKit.Res<Style>("FieldLabel") });
        var placeBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = doc.Data.Places.Select(x => x.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Text = doc.PlaceName(f.PlaceId),
            Margin = new Thickness(0, 0, 0, 12),
        };
        void CommitPlace()
        {
            var name = placeBox.Text.Trim();
            if (name == doc.PlaceName(f.PlaceId)) return;
            doc.Change(() => f.PlaceId = doc.GetOrCreatePlace(name)?.Id, structural: false);
            changed();
        }
        placeBox.LostKeyboardFocus += (_, _) =>
        {
            if (!placeBox.IsKeyboardFocusWithin) CommitPlace();
        };
        placeBox.SelectionChanged += (_, _) => placeBox.Dispatcher.BeginInvoke(new Action(CommitPlace));
        sp.Children.Add(placeBox);

        sp.Children.Add(UiKit.Field(doc, scope == FactScope.Person && !f.Type.HasDate ? "Value" : "Description", () => f.Description, v => f.Description = v, afterChange: changed));

        // The fact's own note (GEDCOM NOTE under the fact), apart from the person's notes.
        sp.Children.Add(UiKit.Field(doc, "Note", () => f.Note, v => f.Note = v, multiline: true, afterChange: changed));

        var checks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var preferred = new CheckBox { Content = "Preferred", IsChecked = f.Preferred, Margin = new Thickness(0, 0, 20, 0), ToolTip = "Use this fact when several of the same kind exist" };
        preferred.Click += (_, _) =>
        {
            doc.Change(() =>
            {
                var on = preferred.IsChecked == true;
                if (on) foreach (var other in facts.Where(x => x.TypeId == f.TypeId)) other.Preferred = false;
                f.Preferred = on;
                person?.RaiseFactsChanged();
            }, structural: false);
            changed();
        };
        var priv = new CheckBox { Content = "Private", IsChecked = f.Private, ToolTip = "Leave this fact out of shared exports and reports" };
        priv.Click += (_, _) =>
        {
            doc.Change(() => f.Private = priv.IsChecked == true, structural: false);
            changed();
        };
        checks.Children.Add(preferred);
        checks.Children.Add(priv);
        sp.Children.Add(checks);

        sp.Children.Add(UiKit.Text("SOURCES", "SectionLabel"));
        if (f.Citations.Count == 0)
        {
            var none = UiKit.Text("No sources cited for this fact yet.", "Caption");
            none.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(none);
        }
        foreach (var c in f.Citations.ToList())
        {
            var src = doc.GetSource(c.SourceId);
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = UiKit.Icon("Book", 15, UiKit.Brush("Brand"));
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.Margin = new Thickness(0, 2, 8, 0);
            g.Children.Add(ic);
            var t = new StackPanel { Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ToolTip = "Edit this citation" };
            t.Children.Add(UiKit.Text(src?.ToString() ?? "(missing source)"));
            if (c.Detail.Length > 0) t.Children.Add(UiKit.Text(c.Detail, "Caption"));
            // The record online, when it was looked at, and the start of its transcription (TDD 25).
            if (c.Url.Length > 0) t.Children.Add(Dialogs.WebLink(c.Url));
            if (c.Accessed.Length > 0) t.Children.Add(UiKit.Text($"Accessed {GenDate.Parse(c.Accessed).Display}", "Caption"));
            if (c.Text.Length > 0)
            {
                var extract = UiKit.Text($"“{c.Text.ReplaceLineEndings(" ")}”", "Caption");
                extract.FontStyle = FontStyles.Italic;
                extract.ToolTip = c.Text;
                t.Children.Add(extract);
            }
            t.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is System.Windows.Documents.Run) return;
                if (Dialogs.Citation(doc, c) is not { } edited) return;
                doc.Change(() =>
                {
                    c.Detail = edited.Detail;
                    c.Url = edited.Url;
                    c.Accessed = edited.Accessed;
                    c.Text = edited.Text;
                }, structural: false);
                rebuild();
            };
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var rm = UiKit.IconButton("Close", "Remove this citation", (_, _) =>
            {
                doc.Change(() => f.Citations.Remove(c), structural: false);
                rebuild();
            });
            rm.Width = rm.Height = 26;
            Grid.SetColumn(rm, 2);
            g.Children.Add(rm);
            sp.Children.Add(g);
        }

        var bottom = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var del = UiKit.Button("GhostButton", "Delete fact", (_, _) =>
        {
            doc.Change(() => facts.Remove(f), structural: false);
            person?.RaiseFactsChanged();
            deleted();
        }, "Trash");
        del.Foreground = UiKit.Brush("Danger");
        DockPanel.SetDock(del, Dock.Right);
        bottom.Children.Add(del);
        var addSource = UiKit.Button("GhostButton", "Add source", (_, _) =>
        {
            if (Dialogs.Citation(doc) is not { } result) return;
            doc.Change(() =>
            {
                if (result.IsNew) doc.Data.Sources.Add(result.Source);
                f.Citations.Add(new Citation { SourceId = result.Source.Id, Detail = result.Detail, Url = result.Url, Accessed = result.Accessed, Text = result.Text });
            }, structural: false);
            rebuild();
        }, "Plus");
        addSource.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(addSource);
        sp.Children.Add(bottom);
        return sp;
    }
}
