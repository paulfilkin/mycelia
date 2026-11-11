using System.Windows;
using System.Windows.Controls;
using Mycelia.Ai;
using Mycelia.Ai.Mcp;

namespace Mycelia.Ui;

/// <summary>
/// Options › MCP servers, browser sign-in (McpOAuth, TDD 22.5): for a server at a web address that answers with a
/// sign-in page of its own.  The client is the person's own app (client ID and secret from the service's developer
/// console, with the callback address registered as its redirect address) or, where the service allows it, one
/// registered automatically.  Ported from multifariousStudio's Options window.
/// </summary>
public sealed partial class OptionsDialog
{
    private CheckBox _oauthEnabled = null!;
    private StackPanel _oauthFields = null!;
    private TextBox _oauthClientId = null!;
    private PasswordBox _oauthSecret = null!;
    private TextBox _oauthCallback = null!;
    private TextBox _oauthScopes = null!;
    private TextBox _oauthExtra = null!;
    private CheckBox _oauthResource = null!;
    private Button _signInButton = null!;
    private TextBlock _oauthStatus = null!;
    private CancellationTokenSource? _signInCancel;

    /// <summary>Servers signed in or out in this window: their tokens are this window's, not the saved ones.</summary>
    private readonly HashSet<string> _oauthTouched = new(StringComparer.OrdinalIgnoreCase);

    private FrameworkElement BuildBrowserSignIn()
    {
        var panel = new StackPanel();
        _oauthEnabled = new CheckBox { Content = "Sign in with the browser (OAuth)", Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(Row("Browser sign-in", _oauthEnabled, "For servers that answer with a sign-in page of their own; the MCP Helper ticks it when a server asks."));

        _oauthFields = new StackPanel { Visibility = Visibility.Collapsed };
        _oauthClientId = Box();
        _oauthSecret = new PasswordBox();
        _oauthCallback = Box();
        _oauthScopes = Box();
        _oauthExtra = Box(multiLine: true, height: 44);
        _oauthResource = new CheckBox { Content = "Name the server in the sign-in (resource parameter); clear only if the service refuses it", Margin = new Thickness(0, 2, 0, 0) };
        _oauthFields.Children.Add(Row("Client ID", _oauthClientId,
            "Your own app's ID from the service's developer console.  Leave empty to register automatically, which only services that allow it accept."));
        _oauthFields.Children.Add(Row("Client secret", _oauthSecret, "Your own app's secret, if it has one.  Kept encrypted."));
        var callbackRow = new DockPanel();
        var copy = SmallButton("Copy", (_, _) =>
        {
            try { Clipboard.SetText(_oauthCallback.Text.Trim()); }
            catch (System.Runtime.InteropServices.COMException) { }
        });
        copy.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(copy, Dock.Right);
        callbackRow.Children.Add(copy);
        callbackRow.Children.Add(_oauthCallback);
        _oauthFields.Children.Add(Row("Callback address", callbackRow,
            "Register this exact address as your app's redirect address.  Mycelia listens on it only while you sign in."));
        _oauthFields.Children.Add(Row("Scopes", _oauthScopes, "Space-separated.  Empty asks for what the server lists."));
        _oauthFields.Children.Add(Row("Extra parameters", _oauthExtra,
            "name=value, one per line, added to the sign-in address (some services need token_access_type=offline for a lasting sign-in)."));
        _oauthFields.Children.Add(Row(string.Empty, _oauthResource));

        var buttons = new WrapPanel();
        _signInButton = SmallButton("Sign in", async (_, _) => await SignInAsync());
        buttons.Children.Add(_signInButton);
        buttons.Children.Add(SmallButton("Sign out", (_, _) => SignOut()));
        var status = new StackPanel();
        status.Children.Add(buttons);
        _oauthStatus = Hint(string.Empty);
        status.Children.Add(_oauthStatus);
        _oauthFields.Children.Add(Row(string.Empty, status));
        panel.Children.Add(_oauthFields);

        foreach (var box in new[] { _oauthClientId, _oauthCallback, _oauthScopes, _oauthExtra })
            box.TextChanged += (_, _) => CommitServerEditor();
        _oauthSecret.PasswordChanged += (_, _) => CommitServerEditor();
        _oauthResource.Click += (_, _) => CommitServerEditor();
        _oauthEnabled.Click += (_, _) =>
        {
            _oauthFields.Visibility = _oauthEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CommitServerEditor();
        };
        return panel;
    }

    /// <summary>The sign-in fields for the server shown (called while the editor is loading).</summary>
    private void ShowOAuth(McpServerConfig c)
    {
        var o = c.OAuth;
        _oauthEnabled.IsChecked = o.Enabled;
        _oauthFields.Visibility = o.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _oauthClientId.Text = o.ClientId;
        _oauthSecret.Password = SecretStore.Unprotect(o.ClientSecret);
        _oauthCallback.Text = o.CallbackAddress;
        _oauthScopes.Text = o.Scopes;
        _oauthExtra.Text = string.Join(Environment.NewLine, o.ExtraParameters.Select(p => $"{p.Key}={p.Value}"));
        _oauthResource.IsChecked = o.SendResource;
        ShowOAuthStatus(c);
    }

    /// <summary>Writes the sign-in fields into the server being edited (called from CommitServerEditor).</summary>
    private void CommitOAuth(McpServerConfig c)
    {
        var o = c.OAuth;
        o.Enabled = _oauthEnabled.IsChecked == true;
        o.ClientId = _oauthClientId.Text.Trim();
        o.ClientSecret = SecretStore.Protect(_oauthSecret.Password);
        o.CallbackAddress = _oauthCallback.Text.Trim().Length > 0 ? _oauthCallback.Text.Trim() : McpOAuthSettings.DefaultCallback;
        o.Scopes = _oauthScopes.Text.Trim();
        // Extra parameters go into the address as they are: not secrets, so not encrypted.
        o.ExtraParameters = Lines(_oauthExtra.Text)
            .Select(l => (At: l.IndexOf('='), Line: l))
            .Where(p => p.At > 0)
            .GroupBy(p => p.Line[..p.At].Trim())
            .ToDictionary(g => g.Key, g => g.Last().Line[(g.Last().At + 1)..].Trim());
        o.SendResource = _oauthResource.IsChecked == true;
    }

    private void ShowOAuthStatus(McpServerConfig? config)
    {
        var oauth = config?.OAuth;
        _oauthStatus.Text = oauth is not { IsSignedIn: true } ? "Not signed in."
            : "Signed in" + (oauth.ExpiresUtc is { } until ? $", token valid until {until.ToLocalTime():g}" : string.Empty)
              + (oauth.RefreshToken.Length > 0 ? "; renewed on its own." : "; sign in again when it runs out (no refresh token).")
              + (oauth.GrantedScopes.Length > 0 ? $"  Scopes: {oauth.GrantedScopes}" : string.Empty);
    }

    /// <summary>Signs the server being edited in through the browser; pressed again while waiting, it stops waiting.</summary>
    private async Task SignInAsync()
    {
        if (_signInCancel != null)
        {
            _signInCancel.Cancel();
            return;
        }
        CommitServerEditor();
        if (_editing is not { } config)
            return;
        config.OAuth.Enabled = true;
        _signInCancel = new CancellationTokenSource();
        _signInButton.Content = "Stop waiting";
        _oauthStatus.Text = "Waiting for the browser: sign in there and allow Mycelia.";
        try
        {
            await McpOAuth.SignInAsync(config, uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.OriginalString) { UseShellExecute = true }),
                line => _hub?.Log($"[{config.Name}] {line}"), _signInCancel.Token);
            _oauthTouched.Add(config.Id);
            ShowOAuthStatus(config);
            _testResult.Text = "Signed in.  Test connection lists its tools; OK keeps the sign-in.";
            Activate();
        }
        catch (OperationCanceledException)
        {
            _oauthStatus.Text = "Stopped waiting for the browser.";
        }
        catch (Exception ex) when (ex is McpException or System.Net.Http.HttpRequestException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _oauthStatus.Text = ex.Message;
        }
        finally
        {
            _signInCancel.Dispose();
            _signInCancel = null;
            _signInButton.Content = "Sign in";
        }
    }

    private void SignOut()
    {
        if (_editing is not { } config)
            return;
        config.OAuth.ClearTokens();
        _oauthTouched.Add(config.Id);
        ShowOAuthStatus(config);
    }

    /// <summary>
    /// Tokens refreshed while this window was open went to the saved servers (MainWindow, McpOAuth.TokensChanged); this
    /// window's copies keep them, unless signed in or out here.
    /// </summary>
    private void KeepSavedTokens()
    {
        foreach (var server in _servers.Where(s => !_oauthTouched.Contains(s.Id)))
            if (_settings.McpServers.FirstOrDefault(s => s.Id == server.Id) is { } saved
                && string.Equals(saved.Url.Trim(), server.Url.Trim(), StringComparison.OrdinalIgnoreCase))
                server.OAuth.TakeTokensFrom(saved.OAuth);
    }
}
