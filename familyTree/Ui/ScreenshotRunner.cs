using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mycelia.Ui;

/// <summary>
/// "--screenshot file.png": open without taking focus and behind other windows, draw about
/// 120 frames, save the window's picture and exit.
/// </summary>
public static class ScreenshotRunner
{
    private const int FramesToDraw = 120;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HwndBottom = new(1);
    private const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010;

    /// <summary>Opens a window behind the others without taking focus; with a size, at that size.</summary>
    public static void Background(Window window, int? width = 1440, int? height = 900)
    {
        window.ShowActivated = false;
        window.Topmost = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 40;
        window.Top = 40;
        if (width is int w) window.Width = Math.Max(window.MinWidth, w);
        if (height is int h) window.Height = Math.Max(window.MinHeight, h);

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
        };
    }

    /// <summary>Takes the window's picture after it has drawn, then exits; a null size keeps the window's own (a dialog's).</summary>
    public static void Prepare(Window window, string path, int? width = 1440, int? height = 900)
    {
        Background(window, width, height);

        var frames = 0;
        var done = false;
        void Capture()
        {
            if (done) return;
            done = true;
            CompositionTarget.Rendering -= OnFrame;
            try
            {
                Save(window, path);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.GetFullPath(path) + ".error.txt", ex.ToString());
            }
            finally
            {
                Application.Current.Shutdown(0);
            }
        }

        void OnFrame(object? sender, EventArgs e)
        {
            frames++;
            if (frames == FramesToDraw) window.Dispatcher.BeginInvoke(new Action(Capture), DispatcherPriority.ApplicationIdle);
        }

        window.ContentRendered += (_, _) =>
        {
            CompositionTarget.Rendering += OnFrame;
            // A covered window may draw slowly; never wait for ever.
            var fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            fallback.Tick += (_, _) => { fallback.Stop(); Capture(); };
            fallback.Start();
        };
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        // The content is drawn at its margin's offset (a dialog's), so the picture takes the margin in too.
        var width = (int)Math.Ceiling((root.ActualWidth + root.Margin.Left + root.Margin.Right) * dpi.DpiScaleX);
        var height = (int)Math.Ceiling((root.ActualHeight + root.Margin.Top + root.Margin.Bottom) * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = File.Create(full);
        encoder.Save(stream);
    }
}
