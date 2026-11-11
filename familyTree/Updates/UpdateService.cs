using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Mycelia.Ui;

namespace Mycelia.Updates;

/// <summary>The values from the plugin's "For the application" box (multifarious Updates › Products › Mycelia).</summary>
public static class UpdateConfig
{
    public const string Endpoint = "https://multifarious.filkin.com/wp-json/multifarious-updates/v1/products/mycelia/latest";

    /// <summary>Exactly the slug in the plugin: it is part of the signed message, so a mismatch makes every signature fail.</summary>
    public const string ProductSlug = "mycelia";

    /// <summary>The name in the user agent, the temporary folder and the installer's file name.</summary>
    public const string ProductName = "Mycelia";

    /// <summary>The name people see.</summary>
    public const string DisplayName = "Mycelia";

    /// <summary>
    /// Public keys by key id (ECDSA P-256, PEM).  A new key ships here before the plugin switches to it (key rotation).
    /// A release signed with a key not listed here is refused with the unknown-key message, and nothing is installed.
    /// ft-2026-1: created with New-UpdateKey on 10 November 2026; its private key is on the publisher's computer only
    /// and is never committed or uploaded.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PublicKeys = new Dictionary<string, string>
    {
        ["ft-2026-1"] =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKLk1YtRU2rOVma3TdngqWv11HMFw\n" +
            "7GQJJBWbd8NXRJANOlfLevkGFk6Te+ui8CG2DDPFoyHSiFAfy4hXpFFdOA==\n" +
            "-----END PUBLIC KEY-----",
    };
}

/// <summary>Options › Updates, kept in settings.json (TDD 23).</summary>
public sealed class UpdateSettings
{
    /// <summary>stable or beta.</summary>
    public string Channel { get; set; } = "stable";

    /// <summary>Check silently at start-up (the default); off means only when asked.</summary>
    public bool AutomaticCheck { get; set; } = true;

    /// <summary>When the update server was last asked.</summary>
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>A newer version found and not yet installed, shown as the update notice; empty when none.</summary>
    public string AvailableVersion { get; set; } = string.Empty;

    /// <summary>The version found is required (ours is below its minimumVersion).</summary>
    public bool AvailableRequired { get; set; }
}

/// <summary>What the update flow needs from the main window.</summary>
public interface IUpdateHost
{
    AppSettings Settings { get; }

    /// <summary>Shows the top bar's update notice for a version, or hides it (null).</summary>
    void ShowUpdateNotice(string? version, bool required);

    /// <summary>A line in the status bar.</summary>
    void ShowStatus(string text);

    /// <summary>Asks to save the tree as closing does, then closes the window; false when the person cancelled.</summary>
    bool CloseForUpdate();
}

/// <summary>
/// Software updates from multifarious.filkin.com (the multifariousUpdates plugin): the start-up and manual checks, the
/// offer, and handing over to the installer.  UpdateClient does the asking, downloading and verifying; nothing is
/// installed unless size, SHA-256 and the ECDSA signature all match.  Ported from multifariousStudio's
/// Services/Updates.cs (TDD 23).
/// </summary>
public static class UpdateService
{
    private static UpdateClient? _client;

    public static UpdateClient Client => _client ??= new UpdateClient(UpdateConfig.Endpoint, UpdateConfig.ProductSlug, UpdateConfig.PublicKeys,
        SemanticVersion.Current, UpdateConfig.ProductName);

    /// <summary>Where installers are downloaded; emptied at the next start (a running installer can't delete itself).</summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), UpdateConfig.ProductName, "Updates");

    /// <summary>The verified installer to start once the program has closed (App.OnExit).</summary>
    public static string? PendingInstaller { get; private set; }

    /// <summary>--noautoupdate on the command line: start without the automatic check.</summary>
    public static bool NoAutoUpdate { get; set; }

    /// <summary>
    /// Start-up, silent: a newer version shows the update notice in the top bar, which the person clicks to download
    /// and install it.  A version found earlier and not yet installed shows the notice straight away and no check is
    /// made: the notice already offers it, and clicking it checks again.  Nothing in screenshot mode, with
    /// --noautoupdate, or when Options › Updates is set to check only when asked.
    /// </summary>
    public static async Task CheckAtStartupAsync(IUpdateHost host)
    {
        var settings = host.Settings;
        if (settings.ReadOnly)
            return;
        CleanTempFolder();
        if (NoAutoUpdate || !settings.Updates.AutomaticCheck)
        {
            ShowRemembered(host);
            return;
        }
        // An update found earlier and not yet acted on is already offered by the notice; otherwise ask at every start.
        if (ShowRemembered(host))
            return;

        var result = await Client.CheckAsync(ChannelOf(settings.Updates.Channel));
        // A failed check is tried again at the next start and leaves the notice as it was.
        if (result.Outcome != UpdateOutcome.Failed)
            Remember(host, result);
    }

    /// <summary>The notice for a version found by an earlier check, while it is still newer than this one; true when shown.</summary>
    private static bool ShowRemembered(IUpdateHost host)
    {
        var u = host.Settings.Updates;
        if (SemanticVersion.TryParse(u.AvailableVersion, out var found) && found! > Client.Current)
        {
            host.ShowUpdateNotice(u.AvailableVersion, u.AvailableRequired);
            return true;
        }
        host.ShowUpdateNotice(null, false);
        return false;
    }

    /// <summary>Keeps a check's answer: when it ran, and the newer version (shown as the notice) or none.</summary>
    private static void Remember(IUpdateHost host, UpdateCheckResult result)
    {
        var u = host.Settings.Updates;
        u.LastCheckUtc = DateTime.UtcNow;
        bool newer = result.Outcome is UpdateOutcome.Available or UpdateOutcome.Required;
        u.AvailableVersion = newer ? result.Info!.Version : string.Empty;
        u.AvailableRequired = result.Outcome == UpdateOutcome.Required;
        host.Settings.Save();
        ShowRemembered(host);
    }

    /// <summary>
    /// The main menu's Check for updates..., the update notice and Options › Updates › Check now: always checks and
    /// reports every outcome.  A newer version is offered at once; Later leaves the notice showing.
    /// </summary>
    public static async Task CheckManuallyAsync(Window owner, IUpdateHost host, string? channel = null)
    {
        var settings = host.Settings;
        channel = ChannelOf(channel ?? settings.Updates.Channel);
        host.ShowStatus("Checking for updates…");
        var cursor = owner.Cursor;
        owner.Cursor = Cursors.AppStarting;
        UpdateCheckResult result;
        try
        {
            result = await Client.CheckAsync(channel);
        }
        finally
        {
            owner.Cursor = cursor;
        }
        // Remembered only for the saved channel: Check now on another channel (not yet saved with OK) leaves the notice alone.
        if (result.Outcome != UpdateOutcome.Failed && channel == ChannelOf(settings.Updates.Channel))
            Remember(host, result);
        else if (result.Outcome != UpdateOutcome.Failed)
        {
            settings.Updates.LastCheckUtc = DateTime.UtcNow;
            settings.Save();
        }
        host.ShowStatus(string.Empty);

        switch (result.Outcome)
        {
            case UpdateOutcome.Available:
            case UpdateOutcome.Required:
                Offer(owner, host, result);
                break;
            case UpdateOutcome.UpToDate:
            case UpdateOutcome.NoRelease:
                MessageBox.Show(owner, $"You have the latest version of {UpdateConfig.DisplayName} ({Client.Current}).", "Software update", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case UpdateOutcome.Failed:
                MessageBox.Show(owner, $"{UpdateConfig.DisplayName} couldn't check for updates: {result.Error}", "Software update", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
    }

    /// <summary>
    /// The offer: download and verify in the update window, then ask to save the tree as closing does (Cancel stops the
    /// update with nothing changed), close the window and let App.OnExit start the installer, after the MCP servers have
    /// stopped and the Assistant's conversation is saved.
    /// </summary>
    public static void Offer(Window owner, IUpdateHost host, UpdateCheckResult result)
    {
        var window = new UpdateWindow(Client, result.Info!, result.Outcome == UpdateOutcome.Required, TempFolder, UpdateConfig.ProductName + "Setup") { Owner = owner };
        if (window.ShowDialog() != true || window.InstallerPath is not { } installer)
            return;

        PendingInstaller = installer;
        host.ShowStatus($"Installing the update: {UpdateConfig.DisplayName} closes and starts again.");
        if (!host.CloseForUpdate())
        {
            PendingInstaller = null;
            host.ShowStatus("The update was cancelled.  Check for updates again when you're ready.");
        }
    }

    /// <summary>App.OnExit: starts the verified installer silently; it closes anything left and starts the program again.</summary>
    public static void StartPendingInstaller()
    {
        if (PendingInstaller is not { } path || !File.Exists(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>beta, or stable for anything else (the endpoint knows only those two).</summary>
    public static string ChannelOf(string? channel) => channel == "beta" ? "beta" : "stable";

    /// <summary>Installers from earlier updates.</summary>
    private static void CleanTempFolder()
    {
        try
        {
            if (Directory.Exists(TempFolder))
                foreach (var file in Directory.EnumerateFiles(TempFolder))
                    File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
