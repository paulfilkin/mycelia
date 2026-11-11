using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;

namespace Mycelia.Ai.Server;

/// <summary>Options › Other programs, kept in settings.json (TDD 24).</summary>
public sealed class McpServerSettings
{
    public const int DefaultPort = 47821;

    /// <summary>Let other programs on this computer use the tools; off by default.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = DefaultPort;

    /// <summary>The bearer token every request must carry, DPAPI-encrypted (SecretStore); made when first switched on.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>A new random token: 32 bytes, Base64url.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What the server needs from the application: the tools, their instructions, and a way to run one.</summary>
public interface IMcpServerHost
{
    /// <summary>The tools offered now (those switched Off are left out).</summary>
    IReadOnlyList<AssistantTool> Tools();

    /// <summary>The rules for a client's model (the initialize result's instructions).</summary>
    string Instructions();

    /// <summary>Runs a tool for a client, on the UI thread, asking the person first where its policy says Ask.</summary>
    Task<ToolOutcome> RunAsync(AssistantTool tool, JsonObject arguments, string client, CancellationToken cancel);

    void Log(string line);
}

/// <summary>
/// Mycelia's own tools served to other programs on this computer over MCP Streamable HTTP (TDD 24): POST
/// <c>http://127.0.0.1:&lt;port&gt;/mcp</c> with one JSON-RPC message or a batch; the answer comes back as JSON (no
/// server-sent events: every answer is a single response).  Locked down three ways: it listens on the loopback address
/// only; every request must carry the bearer token; and a request with a web page's Origin or another Host is refused,
/// so a website can't reach it through the browser (DNS rebinding).  Written on a raw socket, as multifariousStudio's
/// browser sign-in listener is, so Windows needs no URL reservation.
/// </summary>
public sealed class McpServer : IAsyncDisposable
{
    public const string EndpointPath = "/mcp";
    public static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    /// <summary>Tools that delete or overwrite, marked with destructiveHint for clients.</summary>
    private static readonly HashSet<string> Destructive =
        ["delete_person", "delete_fact", "merge_people", "remove_citation", "combine_places", "unlink", "replace_text", "undo", "merge_tree_file"];

    private readonly IMcpServerHost _host;
    private readonly int _port;
    private readonly string _token;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    private sealed record Session(string Id, string Client, DateTime Started)
    {
        public DateTime LastSeen { get; set; } = DateTime.Now;
    }

    public McpServer(IMcpServerHost host, int port, string token)
    {
        _host = host;
        _port = port;
        _token = token;
    }

    public int Port => _port;

    /// <summary>A program connected or ended its session.</summary>
    public event Action? ClientsChanged;

    /// <summary>The programs with a session now, by the name they gave.</summary>
    public IReadOnlyList<string> Clients => _sessions.Values.Select(s => s.Client).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Starts listening; throws a SocketException when the port is taken.</summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _stop = new CancellationTokenSource();
        _ = Task.Run(() => AcceptAsync(_stop.Token));
        _host.Log($"[server] Listening on http://127.0.0.1:{_port}{EndpointPath}");
    }

    private async Task AcceptAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(stop); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { break; }
            _ = Task.Run(() => ServeAsync(client, stop));
        }
    }

    // ---- HTTP ----------------------------------------------------------------------------------------

    private sealed record Request(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                // Keep-alive: several requests may come on one connection.
                while (!stop.IsCancellationRequested && await ReadRequestAsync(stream, stop) is { } request)
                {
                    var (status, headers, body) = await HandleAsync(request, stop);
                    await WriteResponseAsync(stream, status, headers, body, stop);
                    if (request.Headers.TryGetValue("connection", out var c) && c.Equals("close", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
            }
        }
    }

    /// <summary>One HTTP/1.1 request: the request line, headers to a blank line, and a body of Content-Length bytes.</summary>
    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken stop)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, stop);
            if (read == 0) return null;
            head.Add(one[0]);
            if (head.Count >= 4 && head[^1] == '\n' && head[^2] == '\r' && head[^3] == '\n' && head[^4] == '\r') break;
            if (head.Count > 32 * 1024) throw new InvalidDataException("Headers too long.");
        }
        var lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ');
        if (first.Length < 2) throw new InvalidDataException("Not an HTTP request.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int at = line.IndexOf(':');
            if (at > 0) headers[line[..at].Trim()] = line[(at + 1)..].Trim();
        }
        var body = Array.Empty<byte>();
        if (headers.TryGetValue("content-length", out var lengthText) && int.TryParse(lengthText, out int length) && length > 0)
        {
            if (length > 8 * 1024 * 1024) throw new InvalidDataException("Body too long.");
            body = new byte[length];
            int done = 0;
            while (done < length)
            {
                int read = await stream.ReadAsync(body.AsMemory(done, length - done), stop);
                if (read == 0) throw new IOException("The connection closed mid-request.");
                done += read;
            }
        }
        return new Request(first[0].ToUpperInvariant(), first[1], headers, body);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, Dictionary<string, string> headers, string? body, CancellationToken stop)
    {
        var bytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
        var text = new StringBuilder($"HTTP/1.1 {status} {Reason(status)}\r\n");
        foreach (var (key, value) in headers) text.Append(key).Append(": ").Append(value).Append("\r\n");
        if (body is not null) text.Append("Content-Type: application/json; charset=utf-8\r\n");
        text.Append("Content-Length: ").Append(bytes.Length).Append("\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), stop);
        await stream.WriteAsync(bytes, stop);
        await stream.FlushAsync(stop);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
        404 => "Not Found", 405 => "Method Not Allowed", _ => "Error",
    };

    private static (int, Dictionary<string, string>, string?) Plain(int status, string message, Dictionary<string, string>? headers = null) =>
        (status, headers ?? [], new JsonObject { ["error"] = message }.ToJsonString());

    /// <summary>The locks, then the MCP endpoint: POST a message or batch, DELETE a session; GET (an event stream) isn't offered.</summary>
    private async Task<(int Status, Dictionary<string, string> Headers, string? Body)> HandleAsync(Request request, CancellationToken stop)
    {
        // A request through a browser for another site's page (DNS rebinding) names some other host.
        if (!request.Headers.TryGetValue("host", out var host) || !IsLocalHost(host))
            return Plain(403, "Only requests to 127.0.0.1 or localhost are accepted.");
        // Browsers send Origin with a web page's requests; programs such as Claude Desktop don't.
        if (request.Headers.TryGetValue("origin", out var origin) && !IsLocalOrigin(origin))
            return Plain(403, "Requests from web pages are refused.");
        if (!Authorised(request.Headers.GetValueOrDefault("authorization")))
            return Plain(401, "The bearer token is missing or wrong: copy it from Mycelia's Options › Other programs.",
                new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer realm=\"Mycelia\"" });
        var path = request.Target.Split('?')[0];
        if (!path.Equals(EndpointPath, StringComparison.Ordinal))
            return Plain(404, $"The MCP endpoint is {EndpointPath}.");

        var sessionId = request.Headers.GetValueOrDefault("mcp-session-id");
        switch (request.Method)
        {
            case "DELETE":
                if (sessionId is not null && _sessions.TryRemove(sessionId, out var ended))
                {
                    _host.Log($"[server] {ended.Client} ended its session.");
                    ClientsChanged?.Invoke();
                }
                return (200, [], null);
            case "GET":
                return (405, new Dictionary<string, string> { ["Allow"] = "POST, DELETE" }, null);
            case "POST":
                return await PostAsync(request, sessionId, stop);
            default:
                return (405, new Dictionary<string, string> { ["Allow"] = "POST, DELETE" }, null);
        }
    }

    private bool IsLocalHost(string host)
    {
        string name;
        string? port = null;
        if (host.StartsWith('['))
        {
            int end = host.IndexOf(']');
            if (end < 0) return false;
            name = host[..(end + 1)];
            if (host.Length > end + 2 && host[end + 1] == ':') port = host[(end + 2)..];
        }
        else
        {
            var parts = host.Split(':');
            name = parts[0];
            if (parts.Length == 2) port = parts[1];
            else if (parts.Length > 2) return false;
        }
        return (name.Equals("127.0.0.1") || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "[::1]")
               && (port is null || port == _port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1");

    private bool Authorised(string? header)
    {
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header[7..].Trim());
        var expected = Encoding.UTF8.GetBytes(_token);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
    }

    // ---- JSON-RPC ------------------------------------------------------------------------------------

    private async Task<(int, Dictionary<string, string>, string?)> PostAsync(Request request, string? sessionId, CancellationToken stop)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(request.Body); }
        catch (JsonException) { return (400, [], Error(null, -32700, "The body isn't JSON.").ToJsonString()); }
        var messages = node is JsonArray batch ? batch.OfType<JsonObject>().ToList() : node is JsonObject single ? [single] : [];
        if (messages.Count == 0) return (400, [], Error(null, -32600, "Expected a JSON-RPC message.").ToJsonString());

        var headers = new Dictionary<string, string>();
        bool initialising = messages.Any(m => m["method"]?.GetValue<string>() == "initialize");
        Session? session = null;
        if (!initialising)
        {
            if (sessionId is null) return Plain(400, "Mcp-Session-Id is missing: start with initialize.");
            if (!_sessions.TryGetValue(sessionId, out session)) return Plain(404, "Unknown or ended session: start again with initialize.");
            session.LastSeen = DateTime.Now;
        }

        var answers = new JsonArray();
        foreach (var message in messages)
        {
            var id = message["id"]?.DeepClone();
            var method = message["method"]?.GetValue<string>();
            if (method is null) continue;   // a response from the client: nothing is asked of it
            if (id is null) continue;       // a notification (initialized, cancelled): nothing to answer
            JsonObject answer;
            try
            {
                if (method == "initialize")
                {
                    var (result, newSession) = Initialise(message["params"] as JsonObject);
                    session = newSession;
                    headers["Mcp-Session-Id"] = newSession.Id;
                    answer = Result(id, result);
                }
                else
                {
                    answer = method switch
                    {
                        "ping" => Result(id, new JsonObject()),
                        "tools/list" => Result(id, ListTools()),
                        "tools/call" => Result(id, await CallAsync(message["params"] as JsonObject, session!.Client, stop)),
                        _ => Error(id, -32601, $"Mycelia doesn't offer {method}."),
                    };
                }
            }
            catch (ArgumentException ex)
            {
                answer = Error(id, -32602, ex.Message);
            }
            answers.Add(answer);
        }
        if (answers.Count == 0) return (202, headers, null);
        return (200, headers, (node is JsonArray ? answers : answers[0]!).ToJsonString());
    }

    private (JsonObject Result, Session Session) Initialise(JsonObject? parameters)
    {
        var asked = parameters?["protocolVersion"]?.GetValue<string>();
        var info = parameters?["clientInfo"] as JsonObject;
        var client = info?["title"]?.GetValue<string>() ?? info?["name"]?.GetValue<string>() ?? "another program";
        var session = new Session(Guid.NewGuid().ToString("N"), client, DateTime.Now);
        _sessions[session.Id] = session;
        _host.Log($"[server] {client} connected (protocol {asked}).");
        ClientsChanged?.Invoke();
        return (new JsonObject
        {
            ["protocolVersion"] = Versions.Contains(asked) ? asked : Versions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "mycelia",
                ["title"] = "Mycelia",
                ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.2.0",
            },
            ["instructions"] = _host.Instructions(),
        }, session);
    }

    private JsonObject ListTools() => new()
    {
        ["tools"] = new JsonArray(_host.Tools().Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["title"] = t.Title,
            ["description"] = t.Description,
            ["inputSchema"] = t.Schema.DeepClone(),
            ["annotations"] = new JsonObject
            {
                ["title"] = t.Title,
                ["readOnlyHint"] = !t.Changes,
                ["destructiveHint"] = t.Changes && Destructive.Contains(t.Name),
                ["openWorldHint"] = t.ReachesWeb,
            },
        }).ToArray()),
    };

    private async Task<JsonObject> CallAsync(JsonObject? parameters, string client, CancellationToken stop)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new ArgumentException("tools/call needs a tool name.");
        var tool = _host.Tools().FirstOrDefault(t => t.Name == name) ?? throw new ArgumentException($"There is no tool {name} (it may be switched off in Mycelia).");
        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
        _host.Log($"[server] {client} calls {name}");
        ToolOutcome outcome;
        try
        {
            outcome = await _host.RunAsync(tool, (JsonObject)arguments.DeepClone(), client, stop);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException or FormatException)
        {
            outcome = new ToolOutcome(ex.Message, true);
        }
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = outcome.Content }),
            ["isError"] = outcome.IsError,
        };
    }

    private static JsonObject Result(JsonNode? id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    public async ValueTask DisposeAsync()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _sessions.Clear();
        ClientsChanged?.Invoke();
        await Task.CompletedTask;
    }
}
