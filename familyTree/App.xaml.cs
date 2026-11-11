using System.Windows;
using Mycelia.Ui;

namespace Mycelia;

public partial class App : Application
{
    /// <summary>After the window has closed: a verified update starts its installer, which restarts the program (TDD 23).</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Updates.UpdateService.StartPendingInstaller();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --mcp-relay: no window, only the bridge between an MCP client and the running Mycelia (TDD 24).
        if (e.Args.Contains("--mcp-relay"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var portAt = Array.IndexOf(e.Args, "--port");
            int? port = portAt >= 0 && portAt + 1 < e.Args.Length && int.TryParse(e.Args[portAt + 1], out var p) ? p : null;
            Task.Run(() => Ai.Server.McpRelay.RunAsync(port)).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown(0)));
            return;
        }

        string? screenshot = null;
        string? options = null;
        string? open = null;
        string? workspace = null;
        var assistant = false;
        var assistantDemo = false;
        string? size = null;
        string? fold = null;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--screenshot" && i + 1 < e.Args.Length) screenshot = e.Args[++i];
            else if (e.Args[i] == "--workspace" && i + 1 < e.Args.Length) workspace = e.Args[++i];
            else if (e.Args[i] == "--assistant") assistant = true;
            else if (e.Args[i] == "--assistant-demo") assistantDemo = true;
            else if (e.Args[i] == "--size" && i + 1 < e.Args.Length) size = e.Args[++i];
            else if (e.Args[i] == "--fold" && i + 1 < e.Args.Length) fold = e.Args[++i];
            else if (e.Args[i] == "--options" && i + 1 < e.Args.Length) options = e.Args[++i];
            else if (e.Args[i] == "--noautoupdate") Updates.UpdateService.NoAutoUpdate = true;
            else if (!e.Args[i].StartsWith("--", StringComparison.Ordinal)) open = e.Args[i];
        }

        DispatcherUnhandledException += (_, args) =>
        {
            if (screenshot is not null)
            {
                // Keep going: one failed redraw must not stop the picture being taken.
                File.WriteAllText(Path.GetFullPath(screenshot) + ".error.txt", args.Exception.ToString());
                args.Handled = true;
                return;
            }
            MessageBox.Show(args.Exception.Message, "Mycelia", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        try
        {
            var window = new MainWindow(screenshot, open);
            MainWindow = window;
            if (screenshot is not null)
            {
                // --size 1080x680: the window's size for the picture (default 1440x900).
                var parts = (size ?? "").Split('x');
                var (w, h) = parts.Length == 2 && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph) ? (pw, ph) : (1440, 900);
                // --options "Other programs": the picture is of the Options window at that tab instead.
                if (options is null) ScreenshotRunner.Prepare(window, screenshot, w, h);
                else ScreenshotRunner.Background(window, w, h);
            }
            window.Show();
            if (options is not null && screenshot is not null)
            {
                var dialog = window.OptionsForPicture(options);
                ScreenshotRunner.Prepare(dialog, screenshot, null, null);
                dialog.Show();
            }
            if (workspace is not null) window.Navigate(workspace);
            // The Assistant pane, optionally with a sample exchange for check pictures (nothing is sent).
            if (assistant || assistantDemo) window.ShowAssistant(assistantDemo);
            // --fold People.Index,Assistant: fold those panels (screenshot mode, whose settings are never saved).
            if (fold is not null && screenshot is not null) window.FoldPanels(fold.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (Exception ex) when (screenshot is not null)
        {
            File.WriteAllText(Path.GetFullPath(screenshot) + ".error.txt", ex.ToString());
            Shutdown(1);
        }
    }
}
