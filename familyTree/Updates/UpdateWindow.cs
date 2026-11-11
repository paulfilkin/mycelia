using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mycelia.Ui;

namespace Mycelia.Updates;

/// <summary>
/// Offers a newer version: what it is, its release notes (plain text or Markdown, the Markdown shown formatted by
/// ChatText as in the Assistant, never as HTML), and Download and Install, which downloads and verifies the installer
/// here with a progress bar.  The window closes with DialogResult true and <see cref="InstallerPath"/> set only when
/// the file passed every check; closing the tree and starting the installer are the caller's (UpdateService).  Ported
/// from multifariousStudio's Windows/UpdateWindow.cs with this app's theme (TDD 23).
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly UpdateClient _client;
    private readonly UpdateInfo _info;
    private readonly string _folder;
    private readonly string _fileStem;
    private readonly Button _install;
    private readonly Button _later;
    private readonly StackPanel _progressPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1 };
    private readonly TextBlock _progressText = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _cancel;
    private readonly string _installText;

    /// <summary>The verified installer, once Download and Install has finished.</summary>
    public string? InstallerPath { get; private set; }

    public UpdateWindow(UpdateClient client, UpdateInfo info, bool required, string folder, string fileStem)
    {
        _client = client;
        _info = info;
        _folder = folder;
        _fileStem = fileStem;
        Title = "Software update";
        Width = 620;
        Height = 500;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = UiKit.Brush("Surface");
        FontFamily = UiKit.Res<FontFamily>("UiFont");
        FontSize = 13;
        Foreground = UiKit.Brush("Ink");
        UseLayoutRounding = true;

        var root = new DockPanel();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var picture = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top, Source = UiKit.Res<ImageSource>("IconImage") };
        DockPanel.SetDock(picture, Dock.Left);
        top.Children.Add(picture);
        var words = new StackPanel();
        string product = info.Name.Length > 0 ? info.Name : UpdateConfig.DisplayName;
        words.Children.Add(new TextBlock
        {
            Text = $"{product} version {info.Version}" + (required ? " is required" : string.Empty),
            Style = UiKit.Res<Style>("H2"),
            TextWrapping = TextWrapping.Wrap,
        });
        string published = info.PublishedAt is { } at ? $"  Published on {at.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}." : string.Empty;
        string beta = info.ReleaseChannel == "beta" ? "  This is a beta release: an early version that may have problems." : string.Empty;
        words.Children.Add(Note(required
            ? $"Your current version is {client.Current}, which is no longer supported.  Install this version to keep using {UpdateConfig.DisplayName}.{published}"
            : $"An update is available to download and install.  Your current version is {client.Current}.{published}{beta}"));
        top.Children.Add(words);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(_progressPanel);
        _progressPanel.Children.Add(_progress);
        _progressPanel.Children.Add(_progressText);
        _problem.Foreground = UiKit.Brush("Danger");
        bottom.Children.Add(_problem);
        bottom.Children.Add(Note($"Download and Install gets the update and checks that it is the genuine release.  You are then asked to save the tree, as when you close, and {UpdateConfig.DisplayName} closes, updates and starts again."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        _installText = $"Download and Install ({Size(info.Size)})";
        _install = new Button { Style = UiKit.Res<Style>("PrimaryButton"), Content = _installText, IsDefault = true };
        _install.Click += async (_, _) => await InstallAsync();
        _later = new Button { Style = UiKit.Res<Style>("SecondaryButton"), Content = "Later", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        _later.Click += (_, _) =>
        {
            if (_cancel != null)
                _cancel.Cancel();
            else
                Close();
        };
        buttons.Children.Add(_install);
        buttons.Children.Add(_later);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var notes = new DockPanel();
        var notesHeading = new TextBlock { Text = "What's new", Style = UiKit.Res<Style>("H3"), Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(notesHeading, Dock.Top);
        notes.Children.Add(notesHeading);
        if (info.NotesUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var notesUri) && notesUri.Scheme == Uri.UriSchemeHttps)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Release notes")) { NavigateUri = notesUri, Foreground = UiKit.Brush("Brand") };
            link.RequestNavigate += (_, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } };
            var more = new TextBlock(link) { Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(more, Dock.Bottom);
            notes.Children.Add(more);
        }
        // The notes formatted from their Markdown (headings, bold, italic, code, lists, tables, https links), scrolling in a box.
        var text = new ChatText(info.Notes.Trim().Length > 0 ? info.Notes.Trim() : "No release notes were published with this version.");
        notes.Children.Add(new Border
        {
            Background = UiKit.Brush("SurfaceAlt"),
            BorderBrush = UiKit.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 8, 12, 8) },
        });
        root.Children.Add(notes);

        Content = new Border { Padding = new Thickness(20, 18, 20, 18), Child = root };
        Closing += (_, e) =>
        {
            // Closing the window while it downloads stops the download first.
            if (_cancel != null)
            {
                _cancel.Cancel();
                e.Cancel = InstallerPath == null && DialogResult != true;
            }
        };
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Foreground = UiKit.Brush("Muted"),
        Margin = new Thickness(0, 4, 0, 0),
    };

    /// <summary>Megabytes (1024 x 1024 bytes) to one decimal, as multifariousStudio and Trados show a download's size.</summary>
    private static string Size(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1).ToString("F1", CultureInfo.CurrentCulture) + " MB";

    private async Task InstallAsync()
    {
        // While the download runs the button reads "Downloading..." and can't be pressed again.
        _install.Content = "Downloading...";
        _install.IsEnabled = false;
        _later.Content = "Cancel";
        _problem.Visibility = Visibility.Collapsed;
        _progressPanel.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _progressText.Text = "Downloading…";
        Cursor = Cursors.AppStarting;
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                double share = p.Total > 0 ? (double)p.Done / p.Total : 0;
                _progress.Value = share;
                _progressText.Text = p.Done >= p.Total
                    ? "Checking that it is the genuine release…"
                    : $"Downloading… {(int)(share * 100)}% ({Size(p.Done)} of {Size(p.Total)})";
            });
            InstallerPath = await _client.DownloadAndVerifyAsync(_info, _folder, _fileStem, progress, _cancel.Token);
            _progressText.Text = "Verified.";
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            _progressPanel.Visibility = Visibility.Collapsed;
            Show("The download was cancelled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _progressPanel.Visibility = Visibility.Collapsed;
            Show("The update can't be installed: " + ex.Message + "  Nothing was changed.");
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            Cursor = null;
            if (InstallerPath == null)
            {
                _install.Content = _installText;
                _install.IsEnabled = true;
                _later.Content = "Later";
            }
        }
    }

    private void Show(string problem)
    {
        _problem.Text = problem;
        _problem.Visibility = Visibility.Visible;
    }
}
