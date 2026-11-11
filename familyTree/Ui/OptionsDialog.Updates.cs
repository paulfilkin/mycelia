using System.Windows;
using System.Windows.Controls;
using Mycelia.Updates;

namespace Mycelia.Ui;

/// <summary>Options › Updates (TDD 23): automatic or manual check, the stable or beta channel, and Check now.</summary>
public sealed partial class OptionsDialog
{
    private readonly RadioButton _automaticCheck = new() { Content = "Check for updates when Mycelia starts", GroupName = "updateCheck" };
    private readonly RadioButton _manualCheck = new() { Content = "Check only when I ask (Tools > Check for updates...)", GroupName = "updateCheck" };
    private readonly RadioButton _stableChannel = new() { Content = "Stable releases", GroupName = "updateChannel" };
    private readonly RadioButton _betaChannel = new() { Content = "Beta releases as well (early versions that may have problems)", GroupName = "updateChannel" };

    /// <summary>Check now on the channel chosen here, which needn't be saved yet; set by the main window.</summary>
    public Func<string, Task>? CheckForUpdatesNow { get; set; }

    private void AddUpdatesTab(TabControl tabs)
    {
        var u = _settings.Updates;
        _automaticCheck.IsChecked = u.AutomaticCheck;
        _manualCheck.IsChecked = !u.AutomaticCheck;
        _stableChannel.IsChecked = UpdateService.ChannelOf(u.Channel) == "stable";
        _betaChannel.IsChecked = !_stableChannel.IsChecked;

        var last = u.LastCheckUtc is { } at ? $"Last checked {at.ToLocalTime():d MMMM yyyy, HH:mm}." : "Not checked yet.";
        var status = Note($"This is {UpdateConfig.DisplayName} {UpdateService.Client.Current}.  {last}  Updates come from multifarious.filkin.com, and each one is checked against the publisher's signature before it is installed.");
        var checkNow = UiKit.Button("SecondaryButton", "Check now", async (_, _) =>
        {
            if (CheckForUpdatesNow is not null) await CheckForUpdatesNow(_betaChannel.IsChecked == true ? "beta" : "stable");
        });
        checkNow.HorizontalAlignment = HorizontalAlignment.Left;
        checkNow.Margin = new Thickness(0, 14, 0, 0);
        checkNow.IsEnabled = !_settings.ReadOnly;

        tabs.Items.Add(new TabItem { Header = "Updates", Content = Page(
            Label("When to check"), _automaticCheck, _manualCheck, Gap(),
            Label("Which releases"), _stableChannel, _betaChannel,
            checkNow, status) });
    }

    private void ApplyUpdates()
    {
        var u = _settings.Updates;
        u.AutomaticCheck = _automaticCheck.IsChecked == true;
        var channel = _betaChannel.IsChecked == true ? "beta" : "stable";
        // A version found on the other channel no longer applies; the next check finds what this one offers.
        if (channel != UpdateService.ChannelOf(u.Channel))
        {
            u.AvailableVersion = string.Empty;
            u.AvailableRequired = false;
        }
        u.Channel = channel;
    }
}
