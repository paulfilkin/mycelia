using System.Windows;
using System.Windows.Controls;

namespace Mycelia.Ui.Views;

/// <summary>
/// Layout shared by Places, Sources and Media: a searchable list on the left, details on the right, each a panel
/// that folds to a strip (FoldLayout, TDD 16.1).
/// </summary>
public abstract class MasterDetail : UserControl, IWorkspace
{
    protected readonly TextBlock CountText = UiKit.Text("", "Caption");
    protected readonly TextBox Search = new() { Padding = new Thickness(30, 6, 8, 6) };
    protected readonly ListBox List = new();
    protected readonly ContentControl Detail = new();
    protected readonly StackPanel FooterButtons = new() { Orientation = Orientation.Horizontal };
    protected AppState? State;
    private bool _syncing;

    /// <summary>One list row: the thing it stands for and how it looks.</summary>
    protected sealed record Entry(object Model, FrameworkElement Visual);

    /// <param name="title">The workspace and its list panel ("Places").</param>
    /// <param name="listIcon">The list panel's icon.</param>
    /// <param name="detailTitle">The details panel ("Place").</param>
    /// <param name="listWidth">The list panel's starting width.</param>
    protected MasterDetail(string title, string listIcon, string detailTitle, double listWidth = 340)
    {
        List.Style = UiKit.Res<Style>("PlainList");
        List.Padding = new Thickness(8, 0, 8, 8);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(Entry.Visual)));
        List.ItemTemplate = new DataTemplate { VisualTree = presenter };

        var left = new DockPanel();
        var head = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        CountText.Margin = new Thickness(0, 0, 6, 0);
        var searchGrid = new Grid();
        searchGrid.Children.Add(Search);
        var icon = UiKit.Icon("Search", 15, UiKit.Brush("Subtle"));
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.Margin = new Thickness(10, 0, 0, 0);
        icon.IsHitTestVisible = false;
        searchGrid.Children.Add(icon);
        head.Children.Add(searchGrid);
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);

        var footer = new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 10, 12, 10), Child = FooterButtons };
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(footer);
        left.Children.Add(List);

        var layout = new FoldLayout { LayoutKey = title, Margin = new Thickness(12) };
        layout.SetPanels(
            new FoldPanel { Key = "List", Title = title, Icon = listIcon, Side = FoldSide.Left, Size = new GridLength(listWidth), MinSize = 240, HeaderTools = CountText, Body = left },
            new FoldPanel { Key = "Details", Title = detailTitle, Icon = listIcon, Side = FoldSide.Right, MinSize = 360, Body = Detail });
        Content = layout;

        Search.TextChanged += (_, _) => Fill();
        List.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            ShowDetail((List.SelectedItem as Entry)?.Model);
        };
    }

    public virtual void Attach(AppState state)
    {
        State = state;
        state.DocumentReplaced += () => Fill(keepSelection: false);
        state.TreeChanged += () => { if (IsVisible) Fill(); };
    }

    public virtual void OnShown() => Fill();

    protected abstract IEnumerable<Entry> Items(string query);

    protected abstract void ShowDetail(object? item);

    protected virtual string CountLabel(int shown, int total) => shown == total ? $"{total}" : $"{shown} of {total}";

    protected void Fill(bool keepSelection = true)
    {
        if (State is null) return;
        var selected = keepSelection ? KeyOf((List.SelectedItem as Entry)?.Model) : null;
        var q = Search.Text.Trim();
        var total = Items("").Count();
        var items = Items(q).ToList();
        _syncing = true;
        List.ItemsSource = items;
        List.SelectedItem = items.FirstOrDefault(i => selected is not null && KeyOf(i.Model) == selected) ?? items.FirstOrDefault();
        _syncing = false;
        CountText.Text = CountLabel(items.Count, total);
        ShowDetail((List.SelectedItem as Entry)?.Model);
    }

    /// <summary>Fills the list and selects the given model.</summary>
    protected void SelectItem(object model)
    {
        Search.Text = "";
        Fill();
        var entry = (List.ItemsSource as IEnumerable<Entry>)?.FirstOrDefault(i => KeyOf(i.Model) == KeyOf(model));
        if (entry is null) return;
        List.SelectedItem = entry;
        List.ScrollIntoView(entry);
    }

    protected abstract string? KeyOf(object? model);

    protected static ScrollViewer Page(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    protected static FrameworkElement Row(string primary, string secondary, string? trailing = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text(primary, weight: FontWeights.Medium));
        if (secondary.Length > 0) sp.Children.Add(UiKit.Text(secondary, "Caption"));
        grid.Children.Add(sp);
        if (trailing is not null)
        {
            var t = UiKit.Text(trailing, "Caption");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(t, 1);
            grid.Children.Add(t);
        }
        return grid;
    }

    /// <summary>Two text fields side by side.</summary>
    protected static Grid Pair(FrameworkElement a, FrameworkElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(a);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return g;
    }
}
