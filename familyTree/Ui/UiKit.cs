using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>Small building blocks for screens put together in code.</summary>
public static class UiKit
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public static Brush Brush(string key) => Res<Brush>(key);

    public static System.Windows.Shapes.Path Icon(string key, double size = 18, Brush? stroke = null)
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = Res<Geometry>("I." + key),
            Width = size,
            Height = size,
            Style = Res<Style>("Icon"),
        };
        if (stroke is not null) p.Stroke = stroke;
        return p;
    }

    public static StackPanel IconText(string icon, string text, double size = 16)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Icon(icon, size));
        sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    public static Button Button(string style, string text, RoutedEventHandler click, string? icon = null, string? tooltip = null)
    {
        var b = new Button
        {
            Style = Res<Style>(style),
            Content = icon is null ? text : IconText(icon, text),
            ToolTip = tooltip,
        };
        b.Click += click;
        return b;
    }

    public static Button IconButton(string icon, string tooltip, RoutedEventHandler click)
    {
        var b = new Button { Style = Res<Style>("IconButton"), Content = Icon(icon, 17), ToolTip = tooltip };
        b.Click += click;
        return b;
    }

    public static Brush SexBrush(Sex sex) => Brush(sex switch { Sex.Male => "Male", Sex.Female => "Female", _ => "Neutral" });
    public static Brush SexSoftBrush(Sex sex) => Brush(sex switch { Sex.Male => "MaleSoft", Sex.Female => "FemaleSoft", _ => "NeutralSoft" });

    /// <summary>Round portrait: the person's photo, or their initials on a soft colour.</summary>
    public static FrameworkElement Avatar(TreeDocument doc, Person person, double size)
    {
        var photo = PhotoFor(doc, person, (int)Math.Ceiling(size * 3));
        if (photo is not null)
        {
            return new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new ImageBrush(photo) { Stretch = Stretch.UniformToFill },
                Stroke = SexBrush(person.Sex),
                StrokeThickness = Math.Max(1.5, size / 28),
            };
        }
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = SexSoftBrush(person.Sex) });
        grid.Children.Add(new TextBlock
        {
            Text = person.Initials,
            Foreground = SexBrush(person.Sex),
            FontWeight = FontWeights.SemiBold,
            FontSize = Math.Max(10, size * 0.36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    private static readonly Dictionary<string, BitmapSource?> PhotoCache = [];

    public static BitmapSource? PhotoFor(TreeDocument doc, Person person, int pixelWidth)
    {
        var media = doc.GetMedia(person.PhotoMediaId);
        if (media is null || !media.IsImage) return null;
        return LoadImage(media.FilePath, pixelWidth);
    }

    /// <summary>Loads a picture decoded at the size it is shown, so it stays sharp on hi-res screens without wasting memory.</summary>
    public static BitmapSource? LoadImage(string path, int pixelWidth)
    {
        var key = path + "|" + pixelWidth;
        if (PhotoCache.TryGetValue(key, out var cached)) return cached;
        BitmapSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = pixelWidth;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                result = bmp;
            }
        }
        catch (Exception)
        {
            result = null;
        }
        PhotoCache[key] = result;
        return result;
    }

    public static TextBlock Text(string text, string? style = null, double? size = null, Brush? brush = null, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        if (style is not null) t.Style = Res<Style>(style);
        if (size is not null) t.FontSize = size.Value;
        if (brush is not null) t.Foreground = brush;
        if (weight is not null) t.FontWeight = weight.Value;
        return t;
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Style = Res<Style>("Card"), Child = child };
        if (padding is not null) b.Padding = padding.Value;
        return b;
    }

    /// <summary>A labelled text box bound to a getter and setter, with undo grouping.</summary>
    public static FrameworkElement Field(TreeDocument doc, string label, Func<string> get, Action<string> set, bool multiline = false, Action? afterChange = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label, Style = Res<Style>("FieldLabel") });
        var box = new TextBox { Text = get() };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.MinHeight = 90;
            box.VerticalContentAlignment = VerticalAlignment.Top;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        var token = new object();
        box.GotKeyboardFocus += (_, _) => token = new object();
        box.TextChanged += (_, _) =>
        {
            if (box.Text == get()) return;
            doc.BeginEdit(token);
            set(box.Text);
            doc.MarkEdited();
            afterChange?.Invoke();
        };
        box.LostKeyboardFocus += (_, _) => doc.EndEdit();
        panel.Children.Add(box);
        return panel;
    }

    public static FrameworkElement EmptyState(string icon, string title, string text)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        var ic = Icon(icon, 40, Brush("BorderStrong"));
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(ic);
        sp.Children.Add(new TextBlock { Text = title, Style = Res<Style>("H3"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4) });
        sp.Children.Add(new TextBlock { Text = text, Style = Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 280 });
        return sp;
    }
}
