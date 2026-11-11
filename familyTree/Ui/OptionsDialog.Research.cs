using System.Windows;
using System.Windows.Controls;
using Mycelia.Ai.Research;

namespace Mycelia.Ui;

/// <summary>
/// Options › Research (TDD 25.4): the keys for the archive APIs that need one, kept encrypted and added to requests by
/// fetch_url itself so the AI model never sees them, and which archives the Assistant may read without asking.
/// </summary>
public sealed partial class OptionsDialog
{
    private readonly Dictionary<string, Func<string>> _researchKeys = new();

    private void AddResearchTab(TabControl tabs)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(Heading("Research",
            "The Assistant can search these open archives without asking: " + string.Join(", ", ResearchApis.All.Select(a => a.Name)) +
            ".  It asks before reading any other web address.  The names, places and dates in a search go to that archive, as they would if you searched its website yourself."));

        foreach (var api in ResearchApis.All.Where(a => a.NeedsKey))
        {
            var stored = _settings.Assistant.GetResearchKey(api.Id);
            FrameworkElement box;
            if (api.KeyPlace == KeyPlace.Header)
            {
                var password = new PasswordBox { Password = stored };
                _researchKeys[api.Id] = () => password.Password;
                box = password;
            }
            else
            {
                var text = new TextBox { Text = stored };
                _researchKeys[api.Id] = () => text.Text;
                box = text;
            }
            var stack = new StackPanel();
            stack.Children.Add(box);
            var get = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            get.Children.Add(new TextBlock { Text = "Free from ", FontSize = 12, Foreground = UiKit.Brush("Muted") });
            get.Children.Add(Dialogs.WebLink(api.SignUp));
            stack.Children.Add(get);
            panel.Children.Add(Row($"{api.Name.Split(" (")[0]} {api.KeyLabel}", stack));
        }
        panel.Children.Add(Hint("Keys are kept encrypted for your Windows account.  Mycelia adds a key to that archive's own requests, so the AI model never sees it.  " +
                                "The other archives need no key."));

        // Sites always allowed from an approval card ("Always allow this site", TDD 25.4), each with Remove.
        var sites = new StackPanel();
        void ShowSites()
        {
            sites.Children.Clear();
            var kept = _settings.Assistant.AllowedSites.Where(s => !_removedSites.Contains(s)).ToList();
            if (kept.Count == 0)
                sites.Children.Add(Hint("None.  When the Assistant asks to read a web address, \"Always allow this site\" adds the site here."));
            foreach (var site in kept)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var remove = SmallButton("Remove", (_, _) => { _removedSites.Add(site); ShowSites(); });
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = site, VerticalAlignment = VerticalAlignment.Center });
                sites.Children.Add(row);
            }
        }
        ShowSites();
        panel.Children.Add(new Border { Height = 10 });
        panel.Children.Add(Row("Sites always allowed", sites,
            "The Assistant reads these without asking, as it does the archives above.  Any other site still asks."));
        tabs.Items.Add(new TabItem { Header = "Research", Content = Scroll(panel) });
    }

    /// <summary>Sites removed in this window; a site allowed from the Assistant while it was open is kept.</summary>
    private readonly HashSet<string> _removedSites = new(StringComparer.OrdinalIgnoreCase);

    private void ApplyResearch()
    {
        foreach (var (id, value) in _researchKeys)
            _settings.Assistant.SetResearchKey(id, value());
        _settings.Assistant.AllowedSites.RemoveAll(_removedSites.Contains);
    }
}
