using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Mcp;

/// <summary>
/// A Model Context Protocol client: JSON-RPC 2.0 over a local program's standard input and output (stdio)
/// or over Streamable HTTP, written without third-party packages.  It initialises the session, lists the
/// server's tools and calls them.  Requests the server sends to the client are answered: ping with an
/// empty result, roots/list with no roots, anything else with "method not found".  Ported from
/// multifariousStudio's Core/Mcp/McpClient.cs; browser sign-in is not ported yet (TDD 22.5).
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    /// <summary>The protocol revision the client asks for; the server may answer with an older one it supports.</summary>
    public const string ProtocolVersion = "2025-06-18";

    public const string ClientName = "Mycelia";

    private readonly McpServerConfig _config;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _pending = new();
    private IMcpTransport? _transport;
    private long _nextId;

    public McpClient(McpServerConfig config, Action<string>? log = null)
    {
        _config = config;
        _log = line => log?.Invoke($"[{config.Name}] {line}");
    }

    public McpServerConfig Config => _config;
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? NegotiatedVersion { get; private set; }
    public string? Instructions { get; private set; }
    public bool IsConnected => _transport is { IsOpen: true };

    /// <summary>Raised when the server says its tool list changed (notifications/tools/list_changed).</summary>
    public event Action? ToolsChanged;

    /// <summary>Raised when the connection ends on its own (the program stopped, the session expired).</summary>
    public event Action<string>? Closed;

    public async Task ConnectAsync(CancellationToken cancel)
    {
        _transport = _config.Transport == McpTransportKind.Http
            ? new HttpTransport(_config, OnMessage, _log)
            : new StdioTransport(_config, OnMessage, _log);
        _transport.Closed += reason =>
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new McpException(reason));
            Closed?.Invoke(reason);
        };
        await _transport.StartAsync(cancel);

        var result = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = ClientName,
                ["title"] = "Mycelia",
                ["version"] = typeof(McpClient).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"
            }
        }, cancel) as JsonObject;

        NegotiatedVersion = result?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
        ServerName = result?["serverInfo"]?["name"]?.GetValue<string>();
        ServerVersion = result?["serverInfo"]?["version"]?.GetValue<string>();
        Instructions = result?["instructions"]?.GetValue<string>();
        _transport.ProtocolVersion = NegotiatedVersion;
        _log($"Connected: {ServerName} {ServerVersion} (protocol {NegotiatedVersion}).");

        await _transport.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, cancel);
    }

    /// <summary>tools/list, following nextCursor until every page is read.</summary>
    public async Task<List<McpToolInfo>> ListToolsAsync(CancellationToken cancel)
    {
        var tools = new List<McpToolInfo>();
        string? cursor = null;
        for (int page = 0; page < 50; page++)
        {
            var parameters = new JsonObject();
            if (cursor != null)
                parameters["cursor"] = cursor;
            var result = await RequestAsync("tools/list", parameters, cancel) as JsonObject;
            if (result?["tools"] is JsonArray list)
            {
                foreach (var item in list.OfType<JsonObject>())
                {
                    var schema = item["inputSchema"]?.DeepClone() as JsonObject ?? new JsonObject { ["type"] = "object" };
                    if (schema["properties"] == null)
                        schema["properties"] = new JsonObject();
                    tools.Add(new McpToolInfo
                    {
                        Name = item["name"]?.GetValue<string>() ?? string.Empty,
                        Title = item["title"]?.GetValue<string>() ?? item["annotations"]?["title"]?.GetValue<string>(),
                        Description = item["description"]?.GetValue<string>() ?? string.Empty,
                        InputSchema = schema,
                        ReadOnly = item["annotations"]?["readOnlyHint"]?.GetValue<bool>() == true,
                        Destructive = item["annotations"]?["destructiveHint"]?.GetValue<bool>() == true
                    });
                }
            }
            cursor = result?["nextCursor"]?.GetValue<string>();
            if (string.IsNullOrEmpty(cursor))
                break;
        }
        return tools.Where(t => t.Name.Length > 0).ToList();
    }

    /// <summary>tools/call: text content joined; other content (images, resources) described by type.</summary>
    public async Task<McpCallResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancel)
    {
        var result = await RequestAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments.DeepClone() }, cancel) as JsonObject;
        bool isError = result?["isError"]?.GetValue<bool>() == true;
        var builder = new StringBuilder();
        if (result?["content"] is JsonArray content)
        {
            foreach (var part in content.OfType<JsonObject>())
            {
                string type = part["type"]?.GetValue<string>() ?? "text";
                if (builder.Length > 0)
                    builder.Append('\n');
                switch (type)
                {
                    case "text":
                        builder.Append(part["text"]?.GetValue<string>());
                        break;
                    case "resource":
                        builder.Append(part["resource"]?["text"]?.GetValue<string>() ?? $"[resource {part["resource"]?["uri"]}]");
                        break;
                    case "resource_link":
                        builder.Append($"[resource link {part["name"]}: {part["uri"]}]");
                        break;
                    default:
                        builder.Append($"[{type} content, {part["mimeType"]?.GetValue<string>() ?? "no type"}]");
                        break;
                }
            }
        }
        if (builder.Length == 0 && result?["structuredContent"] is JsonNode structured)
            builder.Append(structured.ToJsonString());
        return new McpCallResult(builder.ToString(), isError);
    }

    private async Task<JsonNode?> RequestAsync(string method, JsonObject? parameters, CancellationToken cancel)
    {
        if (_transport == null)
            throw new McpException("Not connected.");
        long id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiter;
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters != null)
            message["params"] = parameters;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_config.TimeoutSeconds, 5, 3600)));
        using var registration = timeout.Token.Register(() => waiter.TrySetCanceled(timeout.Token));
        try
        {
            await _transport.SendAsync(message, timeout.Token);
            var response = await waiter.Task;
            if (response["error"] is JsonObject error)
                throw new McpException(error["message"]?.GetValue<string>() ?? "The server reported an error.", error["code"]?.GetValue<int>());
            return response["result"];
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            if (method == "tools/call")
                await NotifyCancelledAsync(id);
            throw new McpException($"{method} got no answer within {_config.TimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            if (method == "tools/call")
                await NotifyCancelledAsync(id);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task NotifyCancelledAsync(long id)
    {
        try
        {
            if (_transport is { IsOpen: true } transport)
                await transport.SendAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/cancelled",
                    ["params"] = new JsonObject { ["requestId"] = id, ["reason"] = "Cancelled by the user." }
                }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is McpException or IOException or HttpRequestException or InvalidOperationException)
        {
        }
    }

    /// <summary>Every message from the server: a response, a request to the client, or a notification.</summary>
    private void OnMessage(JsonObject message)
    {
        bool hasId = message.TryGetPropertyValue("id", out var idNode) && idNode != null;
        string? method = message["method"]?.GetValue<string>();

        if (method == null && hasId)
        {
            if (TryReadId(idNode!, out long id) && _pending.TryGetValue(id, out var waiter))
                waiter.TrySetResult(message);
            return;
        }

        if (method != null && hasId)
        {
            JsonObject reply = method switch
            {
                "ping" => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = idNode!.DeepClone(), ["result"] = new JsonObject() },
                "roots/list" => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = idNode!.DeepClone(), ["result"] = new JsonObject { ["roots"] = new JsonArray() } },
                _ => new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode!.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Mycelia doesn't support {method}." }
                }
            };
            _ = SendQuietlyAsync(reply);
            return;
        }

        switch (method)
        {
            case "notifications/tools/list_changed":
                ToolsChanged?.Invoke();
                break;
            case "notifications/message":
                _log($"{message["params"]?["level"]}: {message["params"]?["data"]}");
                break;
        }
    }

    private async Task SendQuietlyAsync(JsonObject message)
    {
        try
        {
            if (_transport != null)
                await _transport.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex) when (ex is McpException or IOException or HttpRequestException or InvalidOperationException)
        {
            _log("Couldn't answer the server: " + ex.Message);
        }
    }

    private static bool TryReadId(JsonNode node, out long id)
    {
        id = 0;
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out long number))
            {
                id = number;
                return true;
            }
            if (value.TryGetValue(out string? text) && long.TryParse(text, out number))
            {
                id = number;
                return true;
            }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport != null)
            await _transport.DisposeAsync();
        _transport = null;
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
    }
}

internal interface IMcpTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    string? ProtocolVersion { get; set; }
    event Action<string>? Closed;
    Task StartAsync(CancellationToken cancel);
    Task SendAsync(JsonObject message, CancellationToken cancel);
}

/// <summary>
/// stdio transport: the server is a child process; each message is one line of UTF-8 JSON on its standard
/// input or output, and its standard error is kept as log lines.
/// </summary>
internal sealed class StdioTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly Action<JsonObject> _onMessage;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Queue<string> _lastErrors = new();
    private Process? _process;
    private bool _disposing;

    public StdioTransport(McpServerConfig config, Action<JsonObject> onMessage, Action<string> log)
    {
        _config = config;
        _onMessage = onMessage;
        _log = log;
    }

    public bool IsOpen => _process is { HasExited: false };
    public string? ProtocolVersion { get; set; }
    public event Action<string>? Closed;

    public Task StartAsync(CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(_config.Command))
            throw new McpException("No command is set for this server.");
        var (fileName, prefix) = ResolveCommand(_config.Command.Trim());
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in prefix.Concat(_config.Arguments))
            start.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(_config.WorkingDirectory) && Directory.Exists(_config.WorkingDirectory))
            start.WorkingDirectory = _config.WorkingDirectory;
        foreach (var (key, value) in _config.Environment)
            start.Environment[key] = SecretStore.Unprotect(value);

        _log($"Starting {fileName} {string.Join(" ", start.ArgumentList)}");
        try
        {
            _process = Process.Start(start) ?? throw new McpException($"{_config.Command} didn't start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new McpException($"{_config.Command} couldn't be started: {ex.Message.TrimEnd('.')}. Check the command, or give its full path.");
        }
        _process.StandardInput.NewLine = "\n";
        _process.StandardInput.AutoFlush = false;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            if (_disposing)
                return;
            string errors = string.Join(" | ", _lastErrors);
            int code = SafeExitCode();
            _log($"The server stopped (exit code {code}).");
            Closed?.Invoke($"The server stopped (exit code {code})." + (errors.Length > 0 ? " Its last messages: " + errors : string.Empty));
        };
        _ = Task.Run(ReadOutputAsync);
        _ = Task.Run(ReadErrorsAsync);
        return Task.CompletedTask;
    }

    private int SafeExitCode()
    {
        try { return _process?.ExitCode ?? -1; }
        catch (InvalidOperationException) { return -1; }
    }

    /// <summary>
    /// Finds a command on PATH the way a command prompt would.  Windows runs .cmd and .bat files (npx, uvx
    /// installers) only through cmd.exe, so those are started as cmd.exe /d /s /c.
    /// </summary>
    internal static (string FileName, string[] Prefix) ResolveCommand(string command)
    {
        string? found = null;
        if (Path.IsPathRooted(command) || command.Contains('\\') || command.Contains('/'))
        {
            found = File.Exists(command) ? command : FindWithExtensions(Path.GetFullPath(command));
        }
        else
        {
            var folders = (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var folder in folders)
            {
                string candidate;
                try { candidate = Path.Combine(folder.Trim('"'), command); }
                catch (ArgumentException) { continue; }
                found = Path.HasExtension(command) && File.Exists(candidate) ? candidate : FindWithExtensions(candidate);
                if (found != null)
                    break;
            }
        }
        found ??= command;
        string extension = Path.GetExtension(found);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return ("cmd.exe", new[] { "/d", "/s", "/c", found });
        return (found, Array.Empty<string>());
    }

    private static string? FindWithExtensions(string pathWithoutExtension)
    {
        foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".com" })
        {
            string candidate = pathWithoutExtension + extension;
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private async Task ReadOutputAsync()
    {
        var reader = _process!.StandardOutput;
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { break; }
            if (line == null)
                break;
            if (line.Length == 0)
                continue;
            Dispatch(line, _onMessage, _log);
        }
    }

    internal static void Dispatch(string json, Action<JsonObject> onMessage, Action<string> log)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException)
        {
            log("Not a protocol message: " + (json.Length > 200 ? json[..200] + "…" : json));
            return;
        }
        if (node is JsonObject single)
            onMessage(single);
        else if (node is JsonArray batch)
            foreach (var item in batch.OfType<JsonObject>())
                onMessage(item);
    }

    private async Task ReadErrorsAsync()
    {
        var reader = _process!.StandardError;
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { break; }
            if (line == null)
                break;
            if (line.Trim().Length == 0)
                continue;
            lock (_lastErrors)
            {
                _lastErrors.Enqueue(line.Length > 300 ? line[..300] : line);
                while (_lastErrors.Count > 3)
                    _lastErrors.Dequeue();
            }
            _log("stderr: " + line);
        }
    }

    public async Task SendAsync(JsonObject message, CancellationToken cancel)
    {
        if (_process is not { HasExited: false } process)
            throw new McpException("The server isn't running.");
        await _writeLock.WaitAsync(cancel);
        try
        {
            await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancel);
            await process.StandardInput.FlushAsync(cancel);
        }
        catch (IOException ex)
        {
            throw new McpException("The server closed its input: " + ex.Message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        if (_process == null)
            return;
        try
        {
            // The stdio shutdown: close the server's input, wait, then stop it.
            _process.StandardInput.Close();
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _process.WaitForExitAsync(wait.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        _process.Dispose();
        _process = null;
    }
}

/// <summary>
/// Streamable HTTP transport: every message is POSTed to the server's address; the answer comes back as
/// JSON or as a stream of server-sent events.  The Mcp-Session-Id the server gives at initialise is sent
/// with every later request, with MCP-Protocol-Version, and the session is ended with DELETE.
/// </summary>
internal sealed class HttpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly Action<JsonObject> _onMessage;
    private readonly Action<string> _log;

    /// <summary>
    /// How long to wait for the connection itself, so a server that never answers is reported as such rather than
    /// after the request timeout (multifariousStudio's value, TDD 22.5).
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout }) { Timeout = Timeout.InfiniteTimeSpan };
    private string? _sessionId;
    private bool _open;

    public HttpTransport(McpServerConfig config, Action<JsonObject> onMessage, Action<string> log)
    {
        _config = config;
        _onMessage = onMessage;
        _log = log;
    }

    public bool IsOpen => _open;
    public string? ProtocolVersion { get; set; }
    public event Action<string>? Closed;

    public Task StartAsync(CancellationToken cancel)
    {
        if (!Uri.TryCreate(_config.Url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new McpException("The server address must start with https:// (or http:// for this computer).");
        _open = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// A request with the configured headers, the session id and the protocol version; a browser sign-in's token
    /// replaces any typed Authorization header.
    /// </summary>
    private HttpRequestMessage NewRequest(HttpMethod method, string? token = null)
    {
        var request = new HttpRequestMessage(method, _config.Url.Trim());
        foreach (var (key, value) in _config.Headers)
            if (token == null || !key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(key, SecretStore.Unprotect(value));
        if (token != null)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (_sessionId != null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        if (ProtocolVersion != null)
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        return request;
    }

    public async Task SendAsync(JsonObject message, CancellationToken cancel)
    {
        // With browser sign-in: the token (refreshed first when it has expired); a refused token is refreshed and the
        // request sent once more (McpOAuth).
        bool retried = false;
        string? refreshed = null;
        while (true)
        {
            string? token = refreshed ?? (_config.OAuth.Enabled ? await McpOAuth.AccessTokenAsync(_config, _http, force: false, _log, cancel) : null);
            refreshed = null;
            using var request = NewRequest(HttpMethod.Post, token);
            request.Content = new StringContent(message.ToJsonString(), new UTF8Encoding(false), "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                throw new McpException(Unreachable(ex));
            }

            using (response)
            {
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
                    _sessionId = ids.FirstOrDefault() ?? _sessionId;

                if (response.StatusCode == HttpStatusCode.Unauthorized && token != null && !retried)
                {
                    retried = true;
                    _log("The server refused the token; refreshing it.");
                    refreshed = await McpOAuth.AccessTokenAsync(_config, _http, force: true, _log, cancel);
                    if (refreshed != null)
                        continue;
                }
                if (response.StatusCode == HttpStatusCode.Accepted)
                    return;
                if (response.StatusCode == HttpStatusCode.NotFound && _sessionId != null)
                {
                    _open = false;
                    Closed?.Invoke("The server ended the session; connect again.");
                    throw new McpException("The server ended the session; connect again.");
                }
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync(cancel);
                    if (body.Length > 300)
                        body = body[..300] + "…";
                    string hint = string.Empty;
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        // A server that names its resource metadata signs in through the browser (MCP authorization).
                        bool browser = _config.OAuth.Enabled || McpOAuth.ChallengeParameters(response).ContainsKey("resource_metadata");
                        hint = "  " + (browser ? McpOAuth.SignInNeeded(_config) : "The server needs a sign-in token: add an Authorization header.");
                    }
                    throw new McpException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}. {body}{hint}".Trim());
                }

                string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    await ReadEventsAsync(response, message["id"] != null ? message["id"]!.ToJsonString() : null, cancel);
                    return;
                }
                string json = await response.Content.ReadAsStringAsync(cancel);
                if (json.Trim().Length > 0)
                    StdioTransport.Dispatch(json, _onMessage, _log);
                return;
            }
        }
    }

    /// <summary>Why the server couldn't be reached, in words a person can act on.</summary>
    private string Unreachable(Exception ex)
    {
        var uri = new Uri(_config.Url.Trim());
        string where = $"{uri.Host} (port {uri.Port})";
        var socket = ex.InnerException as System.Net.Sockets.SocketException ?? ex.InnerException?.InnerException as System.Net.Sockets.SocketException;
        if (ex is OperationCanceledException || ex.InnerException is TimeoutException || socket?.SocketErrorCode == System.Net.Sockets.SocketError.TimedOut)
            return $"{where} didn't answer within {ConnectTimeout.TotalSeconds:F0} seconds. The server may only accept connections from certain networks: check the address, or ask its administrator.";
        if (socket?.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            return $"{uri.Host} wasn't found. Check the address.";
        if (socket?.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
            return $"{where} refused the connection. Check the address and that the server is running.";
        return $"{where} couldn't be reached: {ex.Message}";
    }

    /// <summary>Reads server-sent events until the answer to this request has come (or the stream ends).</summary>
    private async Task ReadEventsAsync(HttpResponseMessage response, string? requestId, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        bool answered = false;
        while (!answered)
        {
            string? line = await reader.ReadLineAsync(cancel);
            if (line == null)
                break;
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
                continue;
            }
            if (line.Length != 0 || data.Length == 0)
                continue;
            string json = data.ToString();
            data.Clear();
            StdioTransport.Dispatch(json, message =>
            {
                _onMessage(message);
                if (requestId != null && message["method"] == null && message["id"]?.ToJsonString() == requestId)
                    answered = true;
            }, _log);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _open = false;
        if (_sessionId != null)
        {
            try
            {
                string? token = _config.OAuth is { Enabled: true, IsSignedIn: true } ? SecretStore.Unprotect(_config.OAuth.AccessToken) : null;
                using var request = NewRequest(HttpMethod.Delete, token);
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var _ = await _http.SendAsync(request, wait.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
            }
        }
        _http.Dispose();
    }
}
