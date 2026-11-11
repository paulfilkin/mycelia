using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mycelia.Ui;

/// <summary>Which way a panel folds: towards the left or the right edge.</summary>
public enum FoldSide { Left, Right }

/// <summary>
/// A workspace panel that can be folded to a narrow strip to make room for the others (TDD 16.1).  Open, it is a
/// card with a header row of fixed height (icon, title, the panel's own controls, the fold button) above its body;
/// folded, it is a strip of <see cref="StripWidth"/> with the fold button, the icon and the title written downwards,
/// and a click anywhere on it opens it again.  Every panel shares the header height, so their header lines align.
/// </summary>
[ContentProperty(nameof(Body))]
public sealed class FoldPanel : Border
{
    public const double HeaderHeight = 48;
    public const double StripWidth = 36;

    private bool _built;
    private bool _folded;
    private readonly ContentControl _bodyHost = new() { Focusable = false };
    private FrameworkElement? _open;
    private FrameworkElement? _strip;

    public FoldPanel()
    {
        Style = UiKit.Res<Style>("Panel");
        ClipToBounds = true;
        Loaded += (_, _) => EnsureBuilt();
    }

    /// <summary>The name kept in the settings with the layout's key ("People.Index").</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The icon's key without "I." (UiKit.Icon).</summary>
    public string Icon { get; set; } = "Tree";
    public FoldSide Side { get; set; } = FoldSide.Left;

    /// <summary>The panel's own controls, at the right of its header.</summary>
    public UIElement? HeaderTools { get; set; }

    public UIElement? Body
    {
        get => _bodyHost.Content as UIElement;
        set => _bodyHost.Content = value;
    }

    /// <summary>The width when open: pixels, or "*" for the panel that takes what is left.</summary>
    public GridLength Size { get; set; } = new(1, GridUnitType.Star);

    /// <summary>The narrowest the panel may be when open.</summary>
    public double MinSize { get; set; } = 220;

    /// <summary>Whether the panel can be folded at all.</summary>
    public bool CanFold { get; set; } = true;

    public bool IsFolded => _folded;

    /// <summary>The person folded or opened the panel (not raised for folds made to find room).</summary>
    public event Action? FoldToggled;

    /// <summary>The panel was folded or opened, by the person or to find room.</summary>
    public event Action? FoldedChanged;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        EnsureBuilt();
    }

    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        _open = BuildOpen();
        _strip = BuildStrip();
        Child = _folded ? _strip : _open;
    }

    public void SetFolded(bool folded, bool byPerson = false)
    {
        EnsureBuilt();
        if (folded && !CanFold) return;
        if (_folded == folded) return;
        _folded = folded;
        Child = folded ? _strip : _open;
        FoldedChanged?.Invoke();
        if (byPerson) FoldToggled?.Invoke();
    }

    private Button? _foldButton;

    /// <summary>Shows or hides the fold button after <see cref="CanFold"/> changes.</summary>
    public void RefreshFoldButtons()
    {
        if (_foldButton is not null) _foldButton.Visibility = CanFold ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button FoldButton(bool opening)
    {
        // The arrow points the way the panel will move: towards its edge to fold, away from it to open.
        var towardsLeft = (Side == FoldSide.Left) != opening;
        var button = UiKit.IconButton(towardsLeft ? "ArrowLeft" : "ArrowRight", opening ? $"Show {Title}" : $"Fold {Title} away", (_, _) => SetFolded(!opening, byPerson: true));
        button.Visibility = CanFold ? Visibility.Visible : Visibility.Collapsed;
        return button;
    }

    private FrameworkElement BuildOpen()
    {
        var header = new DockPanel { Height = HeaderHeight, Margin = new Thickness(16, 0, 8, 0), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (HeaderTools is FrameworkElement tools)
        {
            tools.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(tools);
        }
        _foldButton = FoldButton(opening: false);
        right.Children.Add(_foldButton);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(UiKit.Icon(Icon, 17, UiKit.Brush("Brand")));
        title.Children.Add(new TextBlock { Text = Title, Style = UiKit.Res<Style>("H3"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        header.Children.Add(title);

        var panel = new DockPanel();
        var headerBorder = new Border { BorderBrush = UiKit.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
        DockPanel.SetDock(headerBorder, Dock.Top);
        panel.Children.Add(headerBorder);
        panel.Children.Add(_bodyHost);
        return panel;
    }

    private FrameworkElement BuildStrip()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        stack.Children.Add(FoldButton(opening: true));
        var icon = UiKit.Icon(Icon, 17, UiKit.Brush("Brand"));
        icon.Margin = new Thickness(0, 8, 0, 10);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = Title,
            Style = UiKit.Res<Style>("H3"),
            HorizontalAlignment = HorizontalAlignment.Center,
            LayoutTransform = new RotateTransform(90),
        });
        var strip = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = $"Show {Title}", Child = stack };
        strip.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            SetFolded(false, byPerson: true);
        };
        return strip;
    }
}

/// <summary>
/// Panels side by side with a 12 px gap that is a drag handle between two open panels (TDD 16.1).  The panel whose
/// <see cref="FoldPanel.Size"/> is "*" takes what is left; when it is folded the nearest open panel takes its place.
/// Which panels are folded and the widths dragged to are kept in the settings under <see cref="LayoutKey"/>.  When
/// the space given is narrower than the open panels need, panels are folded from the left until they fit, so nothing
/// is cut off.
/// </summary>
public sealed class FoldLayout : Grid
{
    public const double Gap = 12;

    private readonly List<FoldPanel> _panels = [];
    private readonly List<GridSplitter> _splitters = [];
    private readonly Dictionary<FoldPanel, double> _widths = [];
    private bool _fitting;

    private static readonly List<WeakReference<FoldLayout>> All = [];
    private static AppSettings? _settings;

    public FoldLayout()
    {
        lock (All) All.Add(new WeakReference<FoldLayout>(this));
    }

    /// <summary>The name the layout is kept under in the settings ("People", "Places"...).</summary>
    public string LayoutKey { get; set; } = "";

    public IReadOnlyList<FoldPanel> Panels => _panels;

    /// <summary>
    /// Gives every layout the settings (at start-up, after the workspaces are made) and applies what they hold.  In
    /// screenshot mode the settings are fresh, so every panel starts open.
    /// </summary>
    public static void UseSettings(AppSettings settings)
    {
        _settings = settings;
        foreach (var layout in Live()) layout.ApplySaved();
    }

    /// <summary>Folds the panel with this settings key ("People.Index") wherever it is (the --fold switch).</summary>
    public static void FoldByKey(string key)
    {
        foreach (var layout in Live())
            foreach (var panel in layout._panels.Where(p => string.Equals(layout.KeyFor(p), key, StringComparison.OrdinalIgnoreCase)))
                if (layout._panels.Count(p => !p.IsFolded) > 1) panel.SetFolded(true);
    }

    /// <summary>The layout on show, if the visible workspace has one.</summary>
    public static FoldLayout? Visible() => Live().FirstOrDefault(l => l.IsVisible);

    private static List<FoldLayout> Live()
    {
        lock (All)
        {
            All.RemoveAll(w => !w.TryGetTarget(out _));
            return All.Select(w => w.TryGetTarget(out var l) ? l : null).OfType<FoldLayout>().ToList();
        }
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // Panels written as children in XAML.
        var panels = Children.OfType<FoldPanel>().ToList();
        if (panels.Count > 0) SetPanels(panels.ToArray());
    }

    public void SetPanels(params FoldPanel[] panels)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        _panels.Clear();
        _splitters.Clear();
        for (var i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            panel.EnsureBuilt();
            if (i > 0)
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
                var splitter = new GridSplitter { Width = Gap, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Focusable = false };
                splitter.DragCompleted += (_, _) => SaveWidths();
                SetColumn(splitter, ColumnDefinitions.Count - 1);
                Children.Add(splitter);
                _splitters.Add(splitter);
            }
            ColumnDefinitions.Add(new ColumnDefinition());
            SetColumn(panel, ColumnDefinitions.Count - 1);
            Children.Add(panel);
            _panels.Add(panel);
            if (panel.Size.IsAbsolute) _widths[panel] = panel.Size.Value;
            panel.FoldedChanged += UpdateColumns;
            panel.FoldToggled += SaveFolds;
        }
        ApplySaved();
    }

    private string KeyFor(FoldPanel panel) => $"{LayoutKey}.{(panel.Key.Length > 0 ? panel.Key : panel.Title)}";

    private void ApplySaved()
    {
        if (_settings is not null && LayoutKey.Length > 0)
        {
            foreach (var panel in _panels)
            {
                if (_settings.PanelWidths.TryGetValue(KeyFor(panel), out var width) && panel.Size.IsAbsolute)
                    _widths[panel] = Math.Max(panel.MinSize, width);
                panel.SetFolded(_settings.FoldedPanels.Contains(KeyFor(panel)));
            }
            // At least one panel stays open.
            if (_panels.Count > 0 && _panels.All(p => p.IsFolded)) _panels[^1].SetFolded(false);
        }
        UpdateColumns();
    }

    /// <summary>The panel that takes the space left over: the "*" panel, or the nearest open one when it is folded.</summary>
    private FoldPanel? Fill()
    {
        var open = _panels.Where(p => !p.IsFolded).ToList();
        if (open.Count == 0) return null;
        var star = _panels.FirstOrDefault(p => !p.Size.IsAbsolute) ?? _panels[^1];
        if (!star.IsFolded) return star;
        var at = _panels.IndexOf(star);
        return _panels.Skip(at + 1).FirstOrDefault(p => !p.IsFolded) ?? _panels.Take(at).LastOrDefault(p => !p.IsFolded);
    }

    /// <summary>The width last offered to the layout; panels give up width down to their least to fit it.</summary>
    private double _available = double.PositiveInfinity;

    /// <summary>
    /// The width each open panel other than the fill gets: the width it was given or dragged to, less a share of what
    /// doesn't fit, never below its least.  Shares follow how much each can give.
    /// </summary>
    private Dictionary<FoldPanel, double> FittedWidths(FoldPanel? fill)
    {
        var widths = _panels.Where(p => !p.IsFolded && p != fill)
            .ToDictionary(p => p, p => Math.Max(p.MinSize, _widths.TryGetValue(p, out var w) ? w : p.MinSize));
        if (double.IsInfinity(_available)) return widths;
        var need = widths.Values.Sum() + (fill?.MinSize ?? 0)
                   + _panels.Count(p => p.IsFolded) * FoldPanel.StripWidth + Gap * Math.Max(0, _panels.Count - 1);
        var excess = need - _available;
        var slack = widths.Sum(p => p.Value - p.Key.MinSize);
        if (excess <= 0 || slack <= 0) return widths;
        var share = Math.Min(1, excess / slack);
        foreach (var panel in widths.Keys.ToList())
            widths[panel] -= (widths[panel] - panel.MinSize) * share;
        return widths;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width) && Math.Abs(constraint.Width - _available) > 0.5)
        {
            _available = constraint.Width;
            UpdateColumns();
        }
        // Too narrow for the open panels even at their least: fold some after this pass rather than cut the last one
        // off.  Checked here, because arrange is handed at least the desired width and never sees the shortfall.
        if (!_fitting && IsVisible && !double.IsInfinity(_available) && MinimumWidth > _available + 0.5)
        {
            _fitting = true;
            Dispatcher.BeginInvoke(() => { FoldToFit(_available); _fitting = false; }, DispatcherPriority.Render);
        }
        return base.MeasureOverride(constraint);
    }

    private void UpdateColumns()
    {
        var fill = Fill();
        var fitted = FittedWidths(fill);
        var open = _panels.Count(p => !p.IsFolded);
        for (var i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            var column = ColumnDefinitions[i * 2];
            if (panel.IsFolded)
            {
                column.MinWidth = 0;
                SetWidth(column, new GridLength(FoldPanel.StripWidth));
            }
            else if (panel == fill)
            {
                column.MinWidth = panel.MinSize;
                SetWidth(column, new GridLength(1, GridUnitType.Star));
            }
            else
            {
                column.MinWidth = panel.MinSize;
                SetWidth(column, new GridLength(Math.Round(fitted[panel])));
            }
            // The last open panel can't be folded: something must stay on show.
            panel.CanFold = panel.IsFolded || open > 1;
        }
        for (var i = 0; i < _splitters.Count; i++)
        {
            // A drag handle only between two open panels.
            var both = !_panels[i].IsFolded && !_panels[i + 1].IsFolded;
            _splitters[i].IsEnabled = both;
            // Hidden rather than collapsed: the gap stays, without a grip that does nothing.
            _splitters[i].Visibility = both ? Visibility.Visible : Visibility.Hidden;
            _splitters[i].Cursor = both ? System.Windows.Input.Cursors.SizeWE : null;
        }
        foreach (var panel in _panels) panel.RefreshFoldButtons();
    }

    /// <summary>Sets a column's width only when it changes, so fitting during measure settles at once.</summary>
    private static void SetWidth(ColumnDefinition column, GridLength width)
    {
        if (!column.Width.Equals(width)) column.Width = width;
    }

    /// <summary>The width the panels need as they are: open panels at their least, folded ones as strips, and the gaps.</summary>
    public double MinimumWidth =>
        _panels.Sum(p => p.IsFolded ? FoldPanel.StripWidth : p.MinSize) + Gap * Math.Max(0, _panels.Count - 1);

    /// <summary>Folds panels, from the left and never the last open one, until the open ones fit the width.</summary>
    public void FoldToFit(double width)
    {
        while (MinimumWidth > width + 0.5)
        {
            var candidate = _panels.FirstOrDefault(p => !p.IsFolded && p != Fill()) ;
            if (candidate is null || _panels.Count(p => !p.IsFolded) <= 1) break;
            candidate.SetFolded(true);
        }
    }

    private void SaveWidths()
    {
        var fill = Fill();
        for (var i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            if (panel.IsFolded || panel == fill) continue;
            var width = ColumnDefinitions[i * 2].ActualWidth;
            _widths[panel] = width;
            if (_settings is not null && LayoutKey.Length > 0) _settings.PanelWidths[KeyFor(panel)] = Math.Round(width);
        }
        // The fill panel's width follows the others; keep the columns as they are rather than the drag's star values.
        UpdateColumns();
        _settings?.Save();
    }

    private void SaveFolds()
    {
        if (_settings is null || LayoutKey.Length == 0) return;
        foreach (var panel in _panels)
        {
            _settings.FoldedPanels.Remove(KeyFor(panel));
            if (panel.IsFolded) _settings.FoldedPanels.Add(KeyFor(panel));
        }
        _settings.Save();
    }
}
