using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mycelia.Model;
using Mycelia.Ui.Charts;
using Microsoft.Win32;

namespace Mycelia.Ui.Views;

/// <summary>Printable charts: pedigree, descendants, fan and family group sheet.</summary>
public sealed class ChartsView : UserControl, IWorkspace
{
    private readonly ChartCanvas _chart = new();
    private readonly ScaleTransform _zoom = new(0.8, 0.8);
    private readonly ListBox _kinds = new();
    private readonly TextBlock _rootName = UiKit.Text("", weight: FontWeights.SemiBold);
    private readonly Slider _generations = new() { Minimum = 2, Maximum = 7, Value = ChartCanvas.DefaultGenerations, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly TextBlock _genText = UiKit.Text(ChartCanvas.DefaultGenerations.ToString(), "Caption");
    private readonly CheckBox _colour = new() { Content = "Colour by sex", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly CheckBox _places = new() { Content = "Show birth places", IsChecked = true };
    private AppState? _state;
    private Person? _root;

    private static readonly (ChartKind Kind, string Name, string Icon, string About)[] Kinds =
    [
        (ChartKind.Pedigree, "Pedigree chart", "Pedigree", "Direct ancestors, generation by generation"),
        (ChartKind.Descendants, "Descendant chart", "Descendants", "Children, grandchildren and onwards"),
        (ChartKind.Fan, "Fan chart", "Chart", "Ancestors in a half circle"),
        (ChartKind.FamilyGroupSheet, "Family group sheet", "Family", "A couple, their parents and children"),
    ];

    public ChartsView()
    {
        var options = new StackPanel { Margin = new Thickness(16) };
        options.Children.Add(UiKit.Text("CHART", "SectionLabel"));
        _kinds.Style = UiKit.Res<Style>("PlainList");
        foreach (var k in Kinds)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = UiKit.Icon(k.Icon, 20, UiKit.Brush("Brand"));
            ic.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(ic);
            var t = new StackPanel();
            t.Children.Add(UiKit.Text(k.Name, weight: FontWeights.Medium));
            t.Children.Add(UiKit.Text(k.About, "Caption"));
            row.Children.Add(t);
            _kinds.Items.Add(new ListBoxItem { Content = row, Style = UiKit.Res<Style>("ListRow") });
        }
        _kinds.SelectedIndex = 0;
        _kinds.SelectionChanged += (_, _) => Redraw();
        options.Children.Add(_kinds);

        options.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });
        options.Children.Add(UiKit.Text("STARTING PERSON", "SectionLabel"));
        var rootRow = new DockPanel();
        var change = UiKit.Button("GhostButton", "Change", (_, _) => ChooseRoot());
        DockPanel.SetDock(change, Dock.Right);
        rootRow.Children.Add(change);
        _rootName.VerticalAlignment = VerticalAlignment.Center;
        rootRow.Children.Add(_rootName);
        options.Children.Add(rootRow);

        options.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });
        var genHead = new DockPanel();
        DockPanel.SetDock(_genText, Dock.Right);
        genHead.Children.Add(_genText);
        genHead.Children.Add(UiKit.Text("GENERATIONS", "SectionLabel"));
        options.Children.Add(genHead);
        _generations.ValueChanged += (_, _) => { _genText.Text = ((int)_generations.Value).ToString(); Redraw(); };
        options.Children.Add(_generations);
        options.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });
        options.Children.Add(UiKit.Text("STYLE", "SectionLabel"));
        _colour.Click += (_, _) => Redraw();
        _places.Click += (_, _) => Redraw();
        options.Children.Add(_colour);
        options.Children.Add(_places);

        options.Children.Add(new Border { Style = UiKit.Res<Style>("Divider") });
        var export = UiKit.Button("PrimaryButton", "Save as picture...", (_, _) => ExportPng(), "Export");
        export.HorizontalAlignment = HorizontalAlignment.Stretch;
        options.Children.Add(export);
        var print = UiKit.Button("SecondaryButton", "Print...", (_, _) => Print(), "Print");
        print.HorizontalAlignment = HorizontalAlignment.Stretch;
        print.Margin = new Thickness(0, 8, 0, 0);
        options.Children.Add(print);

        var zoomPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var zoomText = UiKit.Text("80%", "Caption");
        zoomText.Width = 44;
        zoomText.TextAlignment = TextAlignment.Center;
        zoomText.VerticalAlignment = VerticalAlignment.Center;
        void Zoom(double d)
        {
            var z = Math.Clamp(_zoom.ScaleX + d, 0.3, 2.0);
            _zoom.ScaleX = _zoom.ScaleY = z;
            zoomText.Text = $"{Math.Round(z * 100)}%";
        }
        zoomPanel.Children.Add(UiKit.IconButton("ZoomOut", "Zoom out", (_, _) => Zoom(-0.1)));
        zoomPanel.Children.Add(zoomText);
        zoomPanel.Children.Add(UiKit.IconButton("ZoomIn", "Zoom in", (_, _) => Zoom(0.1)));

        _chart.LayoutTransform = _zoom;
        var paper = new Border
        {
            Child = _chart,
            Margin = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.10 },
        };
        var preview = new ScrollViewer
        {
            Content = paper,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = UiKit.Brush("SurfaceAlt"),
        };

        // Options and preview, each a panel that folds to a strip (FoldLayout, TDD 16.1).
        var layout = new FoldLayout { LayoutKey = "Charts", Margin = new Thickness(12) };
        layout.SetPanels(
            new FoldPanel
            {
                Key = "Options", Title = "Options", Icon = "Chart", Side = FoldSide.Left, Size = new GridLength(300), MinSize = 260,
                Body = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            },
            new FoldPanel { Key = "Preview", Title = "Preview", Icon = "Print", Side = FoldSide.Right, MinSize = 360, HeaderTools = zoomPanel, Body = preview });
        Content = layout;
    }

    public void Attach(AppState state)
    {
        _state = state;
        state.DocumentReplaced += () => { _root = null; if (IsVisible) Redraw(); };
        state.TreeChanged += () => { if (IsVisible) Redraw(); };
    }

    /// <summary>Shows a kind of chart for the selected person (the Assistant's show_chart), optionally with a number of generations.</summary>
    public void Show(ChartKind kind, int? generations)
    {
        _root = _state?.Selected ?? _state?.Doc.HomePerson;
        _kinds.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == kind));
        if (generations is int g) _generations.Value = Math.Clamp(g, _generations.Minimum, _generations.Maximum);
        Redraw();
    }

    public void OnShown()
    {
        _root = _state?.Selected ?? _state?.Doc.HomePerson;
        Redraw();
    }

    private void ChooseRoot()
    {
        var p = Dialogs.PickPerson(_state!.Doc, "Starting person", "Whose chart should this be?");
        if (p is null) return;
        _root = p;
        Redraw();
    }

    private void Redraw()
    {
        if (_state is null) return;
        var doc = _state.Doc;
        _root = doc.GetPerson(_root?.Id) ?? _state.Selected ?? doc.HomePerson;
        var kind = Kinds[Math.Max(0, _kinds.SelectedIndex)].Kind;
        _rootName.Text = _root?.DisplayName ?? "Nobody";
        _generations.IsEnabled = kind != ChartKind.FamilyGroupSheet;
        _chart.Doc = doc;
        _chart.Root = _root;
        _chart.Kind = kind;
        _chart.Generations = kind == ChartKind.Descendants ? Math.Min(5, (int)_generations.Value) : (int)_generations.Value;
        _chart.ColourBySex = _colour.IsChecked == true;
        _chart.ShowPlaces = _places.IsChecked == true;
        _chart.Title = _root is null ? "" : kind switch
        {
            ChartKind.Pedigree => $"Ancestors of {_root.DisplayName}",
            ChartKind.Descendants => $"Descendants of {_root.DisplayName}",
            ChartKind.Fan => $"Ancestors of {_root.DisplayName}",
            _ => _root.DisplayName,
        };
        _chart.Redraw();
    }

    private void ExportPng()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save chart as picture",
            Filter = "PNG picture (*.png)|*.png",
            FileName = string.IsNullOrWhiteSpace(_chart.Title) ? "chart" : _chart.Title,
        };
        if (dlg.ShowDialog() != true) return;
        // 300 dots per inch: sharp enough to print.
        const double dpi = 300;
        var size = _chart.DesiredSize;
        var transform = _chart.LayoutTransform;
        _chart.LayoutTransform = Transform.Identity;
        _chart.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _chart.Arrange(new Rect(_chart.DesiredSize));
        size = _chart.DesiredSize;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi / 96), (int)Math.Ceiling(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(_chart);
        _chart.LayoutTransform = transform;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        try
        {
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"The picture could not be saved.\n\n{ex.Message}", "Charts", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Print()
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var transform = _chart.LayoutTransform;
        _chart.LayoutTransform = Transform.Identity;
        _chart.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _chart.DesiredSize;
        var scale = Math.Min(dlg.PrintableAreaWidth / size.Width, dlg.PrintableAreaHeight / size.Height);
        scale = Math.Min(scale, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRectangle(new VisualBrush(_chart), null, new Rect(size));
            dc.Pop();
        }
        _chart.Arrange(new Rect(size));
        dlg.PrintVisual(visual, _chart.Title);
        _chart.LayoutTransform = transform;
    }
}
