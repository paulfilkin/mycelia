using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mycelia.Model;
using Microsoft.Win32;

namespace Mycelia.Ui.Views;

/// <summary>Photos, documents and recordings linked to the tree.</summary>
public sealed class MediaView : MasterDetail
{
    public MediaView() : base("Media", "Image", "Media item")
    {
        FooterButtons.Children.Add(UiKit.Button("SecondaryButton", "Add media", (_, _) => AddMedia(), "Plus"));
    }

    private TreeDocument Doc => State!.Doc;

    protected override IEnumerable<Entry> Items(string query) =>
        Doc.Data.Media
            .Where(m => query.Length == 0 || m.Caption.Contains(query, StringComparison.CurrentCultureIgnoreCase) || m.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(m => m.Caption, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new Entry(m, Tile(m)));

    private FrameworkElement Tile(MediaItem m)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var frame = new Border { Width = 48, Height = 40, CornerRadius = new CornerRadius(6), Background = UiKit.Brush("NeutralSoft"), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        var img = m.IsImage ? UiKit.LoadImage(m.FilePath, 160) : null;
        frame.Child = img is not null ? new Image { Source = img, Stretch = Stretch.UniformToFill } : UiKit.Icon(m.IsImage ? "Image" : "Note", 18, UiKit.Brush("Subtle"));
        grid.Children.Add(frame);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(UiKit.Text(m.Caption.Length > 0 ? m.Caption : Path.GetFileName(m.FilePath), weight: FontWeights.Medium));
        var people = m.PersonIds.Count;
        var sub = m.FileExists ? (people == 1 ? "1 person" : $"{people} people") : "File not found";
        sp.Children.Add(UiKit.Text(sub, "Caption", brush: m.FileExists ? null : UiKit.Brush("Warning")));
        Grid.SetColumn(sp, 1);
        grid.Children.Add(sp);
        return grid;
    }

    protected override string? KeyOf(object? model) => (model as MediaItem)?.Id;

    protected override void ShowDetail(object? item)
    {
        if (item is not MediaItem media)
        {
            var empty = (StackPanel)UiKit.EmptyState("Image", "No media yet", "Add photos, certificates and documents, then link them to the people they show.");
            var add = UiKit.Button("PrimaryButton", "Add media", (_, _) => AddMedia(), "Plus");
            add.HorizontalAlignment = HorizontalAlignment.Center;
            add.Margin = new Thickness(0, 14, 0, 0);
            empty.Children.Add(add);
            Detail.Content = empty;
            return;
        }

        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        var preview = new Border
        {
            Height = 300,
            CornerRadius = new CornerRadius(10),
            Background = UiKit.Brush("SurfaceAlt"),
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 16),
            ClipToBounds = true,
        };
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var img = media.IsImage ? UiKit.LoadImage(media.FilePath, (int)(900 * Math.Max(1, dpi))) : null;
        preview.Child = img is not null
            ? new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(8) }
            : UiKit.EmptyState(media.IsImage ? "Image" : "Note", media.FileExists ? "No preview" : "File not found", media.FilePath);
        sp.Children.Add(preview);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        actions.Children.Add(UiKit.Button("SecondaryButton", "Open file", (_, _) => Open(media), "Open"));
        var relink = UiKit.Button("SecondaryButton", "Find file...", (_, _) => Relink(media), "Search");
        relink.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(relink);
        var del = UiKit.Button("GhostButton", "Remove from tree", (_, _) => Delete(media), "Trash");
        del.Foreground = UiKit.Brush("Danger");
        del.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(del);
        sp.Children.Add(actions);

        sp.Children.Add(UiKit.Field(Doc, "Caption", () => media.Caption, v => media.Caption = v));
        sp.Children.Add(Pair(
            UiKit.Field(Doc, "Date", () => media.Date, v => media.Date = v),
            UiKit.Field(Doc, "Category", () => media.Category, v => media.Category = v)));
        sp.Children.Add(UiKit.Field(Doc, "Description", () => media.Description, v => media.Description = v, multiline: true));
        var path = UiKit.Text(media.FilePath, "Caption");
        path.Margin = new Thickness(0, -4, 0, 14);
        sp.Children.Add(path);

        sp.Children.Add(UiKit.Text("PEOPLE IN THIS ITEM", "SectionLabel"));
        foreach (var pid in media.PersonIds.ToList())
        {
            var p = Doc.GetPerson(pid);
            if (p is null) continue;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var rm = UiKit.IconButton("Unlink", "Unlink this person", (_, _) => Doc.Change(() =>
            {
                media.PersonIds.Remove(pid);
                if (p.PhotoMediaId == media.Id) p.PhotoMediaId = null;
            }));
            DockPanel.SetDock(rm, Dock.Right);
            row.Children.Add(rm);
            var portrait = new CheckBox { Content = "Portrait", IsChecked = p.PhotoMediaId == media.Id, IsEnabled = media.IsImage, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            portrait.Click += (_, _) => Doc.Change(() => p.PhotoMediaId = portrait.IsChecked == true ? media.Id : null);
            DockPanel.SetDock(portrait, Dock.Right);
            row.Children.Add(portrait);
            var av = UiKit.Avatar(Doc, p, 30);
            row.Children.Add(av);
            var name = UiKit.Text(p.DisplayName, weight: FontWeights.Medium);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(name);
            sp.Children.Add(row);
        }
        var link = UiKit.Button("GhostButton", "Link a person", (_, _) =>
        {
            var exclude = media.PersonIds.Select(Doc.GetPerson).OfType<Person>();
            var p = Dialogs.PickPerson(Doc, "Link a person", "Who appears in or is described by this item?", exclude);
            if (p is not null) Doc.Change(() => media.PersonIds.Add(p.Id));
        }, "Link");
        link.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(link);
        Detail.Content = Page(sp);
    }

    private void AddMedia()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add media",
            Multiselect = true,
            Filter = "All media|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.pdf;*.mp3;*.wav;*.mp4;*.txt;*.doc;*.docx|Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        MediaItem? last = null;
        Doc.Change(() =>
        {
            foreach (var file in dlg.FileNames)
            {
                if (Doc.Data.Media.Any(m => string.Equals(m.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
                last = new MediaItem { FilePath = file, Caption = Path.GetFileNameWithoutExtension(file) };
                Doc.Data.Media.Add(last);
            }
        });
        if (last is not null) SelectItem(last);
    }

    private void Relink(MediaItem media)
    {
        var dlg = new OpenFileDialog { Title = "Find the file for this item", FileName = Path.GetFileName(media.FilePath) };
        if (dlg.ShowDialog() != true) return;
        Doc.Change(() => media.FilePath = dlg.FileName);
    }

    private static void Open(MediaItem media)
    {
        if (!media.FileExists) return;
        try { Process.Start(new ProcessStartInfo(media.FilePath) { UseShellExecute = true }); }
        catch (Exception) { /* no program for this file type */ }
    }

    private void Delete(MediaItem media)
    {
        if (!Dialogs.Confirm("Remove media", $"Remove \"{media.Caption}\" from the tree?  The file itself is not deleted.", "Remove")) return;
        Doc.Change(() =>
        {
            foreach (var p in Doc.Data.People.Where(p => p.PhotoMediaId == media.Id)) p.PhotoMediaId = null;
            Doc.Data.Media.Remove(media);
        });
    }
}
