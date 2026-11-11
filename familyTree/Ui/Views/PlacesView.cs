using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>Every place used in the tree, with the events that happened there.</summary>
public sealed class PlacesView : MasterDetail
{
    public PlacesView() : base("Places", "Pin", "Place")
    {
        FooterButtons.Children.Add(UiKit.Button("SecondaryButton", "Add place", (_, _) => AddPlace(), "Plus"));
        var tidy = UiKit.Button("GhostButton", "Remove unused", (_, _) => RemoveUnused(), "Trash");
        tidy.Margin = new Thickness(8, 0, 0, 0);
        FooterButtons.Children.Add(tidy);
    }

    private TreeDocument Doc => State!.Doc;

    private Dictionary<string, int> UsageCounts() =>
        Doc.AllFacts().Where(x => x.Fact.PlaceId is not null).GroupBy(x => x.Fact.PlaceId!).ToDictionary(g => g.Key, g => g.Count());

    protected override IEnumerable<Entry> Items(string query)
    {
        var counts = UsageCounts();
        return Doc.Data.Places
            .Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p =>
            {
                var n = counts.GetValueOrDefault(p.Id);
                var rest = p.Name.Contains(',') ? p.Name[(p.Name.IndexOf(',') + 1)..].Trim() : "";
                return new Entry(p, Row(p.ShortName, rest, n == 1 ? "1 event" : $"{n} events"));
            });
    }

    protected override string? KeyOf(object? model) => (model as Place)?.Id;

    protected override void ShowDetail(object? item)
    {
        if (item is not Place place)
        {
            Detail.Content = UiKit.EmptyState("Pin", "No places yet", "Places appear here as you add them to facts.");
            return;
        }
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(UiKit.Button("SecondaryButton", "Show on map", (_, _) => OpenMap(place), "Pin"));
        var combine = UiKit.Button("SecondaryButton", "Combine with...", (_, _) => Combine(place), "Link");
        combine.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(combine);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Text("PLACE", "SectionLabel"));
        var title = UiKit.Text(place.ShortName, "H1");
        titles.Children.Add(title);
        head.Children.Add(titles);
        sp.Children.Add(head);

        sp.Children.Add(UiKit.Field(Doc, "Full name (most local first, separated by commas)", () => place.Name, v => { place.Name = v; title.Text = place.ShortName; }));
        sp.Children.Add(Pair(
            UiKit.Field(Doc, "Latitude", () => place.Latitude, v => place.Latitude = v),
            UiKit.Field(Doc, "Longitude", () => place.Longitude, v => place.Longitude = v)));
        sp.Children.Add(UiKit.Field(Doc, "Notes", () => place.Notes, v => place.Notes = v, multiline: true));

        var events = Doc.AllFacts().Where(x => x.Fact.PlaceId == place.Id)
            .OrderBy(x => x.Fact.ParsedDate.SortKey ?? long.MaxValue).ToList();
        sp.Children.Add(UiKit.Text(events.Count == 0 ? "EVENTS" : $"EVENTS ({events.Count})", "SectionLabel"));
        if (events.Count == 0) sp.Children.Add(UiKit.Text("No facts use this place.", "Caption"));
        foreach (var (owner, fact) in events)
        {
            var row = new Grid { Margin = new Thickness(-8, 0, -8, 2), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var type = UiKit.Text(fact.Type.Name, "Caption");
            type.Margin = new Thickness(8, 7, 0, 7);
            row.Children.Add(type);
            var who = UiKit.Text(Doc.OwnerName(owner), weight: FontWeights.Medium);
            who.Margin = new Thickness(0, 7, 0, 7);
            Grid.SetColumn(who, 1);
            row.Children.Add(who);
            var date = UiKit.Text(fact.ParsedDate.Display, "Caption");
            date.Margin = new Thickness(0, 7, 8, 7);
            date.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(date, 2);
            row.Children.Add(date);
            row.MouseEnter += (_, _) => row.Background = UiKit.Brush("Hover");
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => GoTo(owner);
            sp.Children.Add(row);
        }
        Detail.Content = Page(sp);
    }

    private void GoTo(object owner)
    {
        var person = owner as Person ?? Doc.GetPerson((owner as Family)?.Partner1Id) ?? Doc.GetPerson((owner as Family)?.Partner2Id);
        if (person is null) return;
        State!.Select(person, focus: true);
        State.Navigate("People");
    }

    private void AddPlace()
    {
        var name = Dialogs.Prompt("Add place", "Place name, most local part first (e.g. Hebden Bridge, Yorkshire, England)", "");
        if (name is null) return;
        Place? created = null;
        Doc.Change(() => created = Doc.GetOrCreatePlace(name));
        if (created is not null) SelectItem(created);
    }

    private void RemoveUnused()
    {
        var before = Doc.Data.Places.Count;
        Doc.Change(Doc.PrunePlaces);
        var removed = before - Doc.Data.Places.Count;
        MessageBox.Show(Window.GetWindow(this), removed == 0 ? "Every place is in use." : $"Removed {removed} unused places.", "Places", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Combine(Place place)
    {
        var others = Doc.Data.Places.Where(p => p != place).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var target = Dialogs.PickItem("Combine places", $"Facts at \"{place.Name}\" will move to the place you choose, and \"{place.Name}\" will be removed.", others, p => p.Name);
        if (target is null) return;
        Doc.Change(() =>
        {
            foreach (var (_, f) in Doc.AllFacts().Where(x => x.Fact.PlaceId == place.Id)) f.PlaceId = target.Id;
            if (target.Latitude.Length == 0) { target.Latitude = place.Latitude; target.Longitude = place.Longitude; }
            Doc.Data.Places.Remove(place);
        });
        SelectItem(target);
    }

    private static void OpenMap(Place place)
    {
        var url = double.TryParse(place.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                  && double.TryParse(place.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            ? $"https://www.openstreetmap.org/?mlat={lat.ToString(CultureInfo.InvariantCulture)}&mlon={lon.ToString(CultureInfo.InvariantCulture)}#map=12/{lat.ToString(CultureInfo.InvariantCulture)}/{lon.ToString(CultureInfo.InvariantCulture)}"
            : $"https://www.openstreetmap.org/search?query={Uri.EscapeDataString(place.Name)}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser available */ }
    }
}
