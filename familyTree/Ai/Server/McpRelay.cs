using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mycelia.Ui;

namespace Mycelia.Ai.Server;

/// <summary>
/// <c>Mycelia.exe --mcp-relay</c> (TDD 24): for MCP clients that only start a program and talk to it on standard input
/// and output, such as Claude Desktop.  It opens no window: it reads each JSON-RPC message from standard input, posts it
/// to the running Mycelia's server with the token from the settings, and writes the answer to standard output.  When
/// Mycelia isn't open, or other programs are switched off, every request is answered with an error saying so.
/// <c>--port</c> and the <c>MYCELIA_MCP_TOKEN</c> environment variable override the settings (for the check program).
/// </summary>
public static class McpRelay
{
    public const string TokenVariable = "MYCELIA_MCP_TOKEN";

    public static async Task RunAsync(int? portOverride)
    {
        var settings = AppSettings.Load();
        var overrideToken = Environment.GetEnvironmentVariable(TokenVariable);
        var token = string.IsNullOrEmpty(overrideToken) ? SecretStore.Unprotect(settings.McpServer.Token) : overrideToken;
        var port = portOverride ?? settings.McpServer.Port;
        string? refusal = string.IsNullOrEmpty(overrideToken) && (!settings.McpServer.Enabled || token.Length == 0)
            ? "Other programs are switched off in Mycelia: turn them on in Options › Other programs, then connect again."
            : null;

        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var writeLock = new SemaphoreSlim(1, 1);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        var endpoint = $"http://127.0.0.1:{port}{McpServer.EndpointPath}";
        string? session = null;
        var running = new List<Task>();

        async Task Write(JsonNode message)
        {
            await writeLock.WaitAsync();
            try { await output.WriteLineAsync(message.ToJsonString()); }
            finally { writeLock.Release(); }
        }

        async Task Relay(string line)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { return; }
            var ids = (node is JsonArray batch ? batch.OfType<JsonObject>() : node is JsonObject single ? [single] : [])
                .Where(m => m["method"] is not null && m["id"] is not null).Select(m => m["id"]!.DeepClone()).ToList();

            async Task Fail(string message)
            {
                foreach (var id in ids)
                    await Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32000, ["message"] = message } });
            }

            if (refusal is not null) { await Fail(refusal); return; }
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(line, new UTF8Encoding(false), "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Fail(ex is TaskCanceledException
                    ? "Mycelia didn't answer in time."
                    : "Mycelia isn't open (or isn't letting other programs in).  Open Mycelia, check Options › Other programs, then try again.");
                return;
            }
            using (response)
            {
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) session = values.FirstOrDefault() ?? session;
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.Accepted || body.Trim().Length == 0) return;
                if (!response.IsSuccessStatusCode && !body.TrimStart().StartsWith("{\"jsonrpc", StringComparison.Ordinal))
                {
                    var why = (JsonNode.Parse(body) as JsonObject)?["error"]?.ToString() ?? response.ReasonPhrase;
                    await Fail(response.StatusCode == HttpStatusCode.NotFound
                        ? "Mycelia was restarted since this connection began: reconnect to start again."
                        : $"Mycelia refused the request: {why}");
                    return;
                }
                await Write(JsonNode.Parse(body)!);
            }
        }

        while (await input.ReadLineAsync() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            // initialize is relayed before anything else is read, so later messages carry the session it starts.
            var task = Relay(line);
            if (line.Contains("\"initialize\"", StringComparison.Ordinal)) await task;
            else running.Add(task);
            running.RemoveAll(t => t.IsCompleted);
        }
        await Task.WhenAll(running);

        // The client has gone: end the session.
        if (session is not null && refusal is null)
        {
            try
            {
                using var end = new HttpRequestMessage(HttpMethod.Delete, endpoint);
                end.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                end.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
                using var _ = await http.SendAsync(end);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }
        }
    }
}
