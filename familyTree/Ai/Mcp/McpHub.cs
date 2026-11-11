using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Mcp;

public enum McpServerStatus
{
    Off,
    Connecting,
    Connected,
    Failed
}

/// <summary>What the app knows about one configured server right now.</summary>
public sealed class McpServerState
{
    public McpServerState(McpServerConfig config) => Config = config;

    public McpServerConfig Config { get; internal set; }
    public McpServerStatus Status { get; internal set; } = McpServerStatus.Off;
    public string Message { get; internal set; } = "Not connected";
    public IReadOnlyList<McpToolInfo> Tools { get; internal set; } = Array.Empty<McpToolInfo>();
    public string? ServerDescription { get; internal set; }

    public string StatusText => Status switch
    {
        McpServerStatus.Connected => $"Connected, {Tools.Count} tools",
        McpServerStatus.Connecting => "Connecting…",
        McpServerStatus.Failed => "Failed: " + Message,
        _ => Config.Enabled ? "Not connected" : "Off"
    };
}

/// <summary>
/// Keeps one connection per enabled MCP server, their tool lists, and a log of what was said.  Everything is
/// asynchronous; <see cref="Changed"/> fires (on any thread) when a status or tool list changes.  Ported from
/// multifariousStudio's Core/Mcp/McpHub.cs.
/// </summary>
public sealed class McpHub : IAsyncDisposable
{
    private const int LogLimit = 1000;
    private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, McpServerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _log = new();

    public event Action? Changed;

    public IReadOnlyList<McpServerState> States => _states.Values.OrderBy(s => s.Config.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public IReadOnlyList<string> LogLines => _log.ToList();

    public void Log(string line)
    {
        _log.Enqueue($"{DateTime.Now:HH:mm:ss}  {line}");
        while (_log.Count > LogLimit && _log.TryDequeue(out _))
        {
        }
    }

    public McpServerState? State(string id) => _states.TryGetValue(id, out var state) ? state : null;

    /// <summary>Brings the connections in line with the settings: removed or disabled servers are closed, the rest connected.</summary>
    public async Task ApplyAsync(IReadOnlyList<McpServerConfig> servers, CancellationToken cancel)
    {
        foreach (var id in _states.Keys.ToList())
        {
            var config = servers.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                await DisconnectAsync(id);
                _states.TryRemove(id, out _);
            }
        }
        var tasks = new List<Task>();
        foreach (var config in servers)
        {
            var state = _states.GetOrAdd(config.Id, _ => new McpServerState(config));
            bool changed = !SameConnection(state.Config, config);
            state.Config = config;
            if (!config.Enabled)
            {
                await DisconnectAsync(config.Id);
                state.Status = McpServerStatus.Off;
                state.Tools = Array.Empty<McpToolInfo>();
                continue;
            }
            if (changed || state.Status is McpServerStatus.Off or McpServerStatus.Failed || !_clients.ContainsKey(config.Id))
                tasks.Add(ConnectAsync(config, cancel));
        }
        Changed?.Invoke();
        await Task.WhenAll(tasks);
    }

    private static bool SameConnection(McpServerConfig a, McpServerConfig b) =>
        a.Transport == b.Transport && a.Command == b.Command && a.Url == b.Url && a.WorkingDirectory == b.WorkingDirectory
        && a.Arguments.SequenceEqual(b.Arguments)
        && a.Environment.Count == b.Environment.Count && a.Environment.All(p => b.Environment.TryGetValue(p.Key, out var v) && v == p.Value)
        && a.Headers.Count == b.Headers.Count && a.Headers.All(p => b.Headers.TryGetValue(p.Key, out var v) && v == p.Value)
        && a.OAuth.Enabled == b.OAuth.Enabled && a.OAuth.ClientId == b.OAuth.ClientId && a.OAuth.IsSignedIn == b.OAuth.IsSignedIn
        && a.OAuth.RefreshToken == b.OAuth.RefreshToken;

    /// <summary>
    /// A reason not to connect to a server, or null: Mycelia's own relay or its own server, which would have the
    /// Assistant calling itself (TDD 24).  Set by the main window.
    /// </summary>
    public Func<McpServerConfig, string?>? Refuse { get; set; }

    public async Task ConnectAsync(McpServerConfig config, CancellationToken cancel)
    {
        await DisconnectAsync(config.Id);
        var state = _states.GetOrAdd(config.Id, _ => new McpServerState(config));
        state.Config = config;
        if (Refuse?.Invoke(config) is { } refusal)
        {
            state.Status = McpServerStatus.Failed;
            state.Message = refusal;
            state.Tools = Array.Empty<McpToolInfo>();
            Log($"[{config.Name}] Not connected: {refusal}");
            Changed?.Invoke();
            return;
        }
        state.Status = McpServerStatus.Connecting;
        state.Message = "Connecting…";
        Changed?.Invoke();

        var client = new McpClient(config, Log);
        try
        {
            await client.ConnectAsync(cancel);
            var tools = await client.ListToolsAsync(cancel);
            client.ToolsChanged += () => _ = RefreshToolsAsync(config.Id);
            client.Closed += reason =>
            {
                state.Status = McpServerStatus.Failed;
                state.Message = reason;
                _clients.TryRemove(config.Id, out _);
                Changed?.Invoke();
            };
            _clients[config.Id] = client;
            state.Tools = tools;
            state.Status = McpServerStatus.Connected;
            state.Message = "Connected";
            state.ServerDescription = $"{client.ServerName} {client.ServerVersion}".Trim() + $" · protocol {client.NegotiatedVersion}";
            Log($"[{config.Name}] {tools.Count} tools: {string.Join(", ", tools.Select(t => t.Name))}");
        }
        catch (Exception ex) when (ex is McpException or IOException or HttpRequestException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException)
        {
            await client.DisposeAsync();
            state.Status = McpServerStatus.Failed;
            state.Message = ex is OperationCanceledException ? "Cancelled" : ex.Message;
            state.Tools = Array.Empty<McpToolInfo>();
            Log($"[{config.Name}] Failed: {state.Message}");
        }
        Changed?.Invoke();
    }

    private async Task RefreshToolsAsync(string id)
    {
        if (!_clients.TryGetValue(id, out var client) || State(id) is not { } state)
            return;
        try
        {
            state.Tools = await client.ListToolsAsync(CancellationToken.None);
            Log($"[{state.Config.Name}] The tool list changed: {state.Tools.Count} tools.");
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is McpException or IOException or HttpRequestException or OperationCanceledException)
        {
            Log($"[{state.Config.Name}] Couldn't read the new tool list: {ex.Message}");
        }
    }

    public async Task DisconnectAsync(string id)
    {
        if (_clients.TryRemove(id, out var client))
            await client.DisposeAsync();
        if (State(id) is { } state && state.Status != McpServerStatus.Failed)
        {
            state.Status = McpServerStatus.Off;
            state.Tools = Array.Empty<McpToolInfo>();
        }
    }

    /// <summary>Calls a tool; a dropped connection is opened again once before giving up.</summary>
    public async Task<McpCallResult> CallAsync(string serverId, string tool, JsonObject arguments, CancellationToken cancel)
    {
        if (!_clients.TryGetValue(serverId, out var client) || !client.IsConnected)
        {
            if (State(serverId) is not { } state || !state.Config.Enabled)
                throw new McpException("That server is switched off in Options › MCP servers.");
            await ConnectAsync(state.Config, cancel);
            if (!_clients.TryGetValue(serverId, out client))
                throw new McpException($"{state.Config.Name} isn't connected: {state.Message}");
        }
        Log($"[{client.Config.Name}] call {tool} {Shorten(arguments.ToJsonString(), 300)}");
        var result = await client.CallToolAsync(tool, arguments, cancel);
        Log($"[{client.Config.Name}] {(result.IsError ? "error" : "result")} {Shorten(result.Text, 300)}");
        return result;
    }

    /// <summary>Connects to a server once, lists its tools and disconnects (the Options window's Test button).</summary>
    public static async Task<(bool Ok, string Message, List<McpToolInfo> Tools)> TestAsync(McpServerConfig config, Action<string> log, CancellationToken cancel)
    {
        var client = new McpClient(config, log);
        try
        {
            await client.ConnectAsync(cancel);
            var tools = await client.ListToolsAsync(cancel);
            return (true, $"Connected to {client.ServerName} {client.ServerVersion} (protocol {client.NegotiatedVersion}): {tools.Count} tools.", tools);
        }
        catch (Exception ex) when (ex is McpException or IOException or HttpRequestException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return (false, ex is OperationCanceledException ? "Cancelled." : ex.Message, new List<McpToolInfo>());
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    internal static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _clients.Keys.ToList())
            await DisconnectAsync(id);
    }
}
