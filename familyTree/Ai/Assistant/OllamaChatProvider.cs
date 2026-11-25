using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Assistant;

/// <summary>
/// Ollama's own chat API (/api/chat), used for Ollama on this computer instead of its OpenAI-compatible one: only this
/// one sets the context size and turns thinking off, and the reply is streamed (TDD 22.2).  Ported from HoboLibris.
/// </summary>
public sealed class OllamaChatProvider : IChatProvider
{
    private const string Name = "Ollama";

    private readonly ChatProviderOptions _options;

    public OllamaChatProvider(ChatProviderOptions options) => _options = options;

    public string DisplayName => _options.Model;

    /// <summary>The chat address on the same host and port as the address in Options (http://localhost:11434/v1).</summary>
    public static Uri ChatUrl(string endpoint)
    {
        var given = new Uri(endpoint.Trim());
        return new Uri($"{given.Scheme}://{given.Authority}/api/chat");
    }

    public async Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel,
        Action<string>? onText = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl(_options.Endpoint))
        {
            Content = new StringContent(Body(_options, system, turns, tools).ToJsonString(), Encoding.UTF8, "application/json")
        };
        var clock = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            // Read as it comes: the reply is a line of JSON per piece.
            response = await ChatProviders.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatException($"{Name} couldn't be reached: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ChatException($"{Name} didn't answer within five minutes.");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw ChatProviders.Error(Name, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancel));
            var reader = new OllamaStream(clock);
            using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(cancel), Encoding.UTF8);
            try
            {
                while (await stream.ReadLineAsync(cancel) is { } line)
                    if (reader.Add(line) is { Length: > 0 } piece)
                        onText?.Invoke(piece);
            }
            catch (IOException ex)
            {
                throw new ChatException($"{Name} couldn't be reached: {ex.Message}");
            }
            return reader.Finish();
        }
    }

    /// <summary>The request: the conversation, the tools, and the options Mycelia sets.</summary>
    public static JsonObject Body(ChatProviderOptions options, string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var turn in turns)
        {
            switch (turn)
            {
                case UserTurn user:
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = user.Text });
                    break;
                case AssistantTurn assistant:
                    if (assistant.Text.Length == 0 && assistant.ToolCalls.Count == 0)
                        break;
                    var message = new JsonObject { ["role"] = "assistant", ["content"] = assistant.Text };
                    // Ollama takes the arguments as an object, not as a JSON string as OpenAI does.
                    if (assistant.ToolCalls.Count > 0)
                        message["tool_calls"] = new JsonArray(assistant.ToolCalls.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id,
                            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.DeepClone() }
                        }).ToArray());
                    messages.Add(message);
                    break;
                case ToolResultsTurn results:
                    foreach (var result in results.Results)
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = result.CallId,
                            ["tool_name"] = result.Name,
                            ["content"] = (result.IsError ? "ERROR: " : string.Empty) + (result.Content.Length == 0 ? "(no output)" : result.Content)
                        });
                    break;
            }
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["think"] = options.Think,
            // A request larger than the context is refused with an error, not cut without a word.
            ["truncate"] = false,
            ["options"] = new JsonObject
            {
                ["num_ctx"] = options.ContextSize,
                ["num_predict"] = options.MaxTokens
            }
        };
        if (tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.InputSchema.DeepClone()
                }
            }).ToArray());
        return body;
    }
}

/// <summary>
/// Reads Ollama's streamed reply, a line of JSON per piece: the text so far, the tool calls, and in the last line (done)
/// why it stopped and the token counts and times.
/// </summary>
public sealed class OllamaStream
{
    private readonly Stopwatch _clock;
    private readonly StringBuilder _text = new();
    private readonly List<ToolCall> _calls = [];
    private TimeSpan? _firstOutput;
    private JsonObject? _last;

    public OllamaStream(Stopwatch clock) => _clock = clock;

    /// <summary>Takes one line; returns the text it adds (empty for none).</summary>
    public string Add(string line)
    {
        if (line.Trim().Length == 0)
            return string.Empty;
        JsonObject json;
        try
        {
            json = JsonNode.Parse(line) as JsonObject ?? throw new ChatException("Ollama sent an answer that isn't JSON.");
        }
        catch (JsonException)
        {
            throw new ChatException("Ollama sent an answer that isn't JSON.");
        }
        // An error once the reply has started comes as a line of its own.
        if (json["error"] is { } error)
            throw new ChatException("Ollama: " + ((error as JsonValue)?.ToString() ?? error.ToJsonString()));
        string piece = (json["message"]?["content"] as JsonValue)?.ToString() ?? string.Empty;
        if (json["message"]?["tool_calls"] is JsonArray calls)
        {
            foreach (var call in calls.OfType<JsonObject>())
            {
                var function = call["function"] as JsonObject;
                _calls.Add(new ToolCall((call["id"] as JsonValue)?.ToString() is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
                    (function?["name"] as JsonValue)?.ToString() ?? string.Empty,
                    Arguments(function?["arguments"])));
            }
        }
        if (_firstOutput is null && (piece.Length > 0 || _calls.Count > 0))
            _firstOutput = _clock.Elapsed;
        _text.Append(piece);
        if (json["done"] is JsonValue done && done.TryGetValue(out bool isDone) && isDone)
            _last = json;
        return piece;
    }

    /// <summary>The whole reply, once the last line has come.</summary>
    public ChatReply Finish()
    {
        if (_last is null)
            throw new ChatException("Ollama sent no answer.");
        int Count(string name) => _last[name] is JsonValue v && v.TryGetValue(out int n) ? n : 0;
        TimeSpan Time(string name) => _last[name] is JsonValue v && v.TryGetValue(out long ns) ? TimeSpan.FromTicks(ns / 100) : TimeSpan.Zero;
        var stats = new ReplyStats(Count("prompt_eval_count"), Count("eval_count"), Time("load_duration"),
            Time("prompt_eval_duration"), Time("eval_duration"), _firstOutput ?? _clock.Elapsed, _clock.Elapsed);
        // Ollama says "stop" when the reply ends with tool calls too.
        string reason = (_last["done_reason"] as JsonValue)?.ToString() ?? string.Empty;
        return new ChatReply(_text.ToString(), _calls, reason, stats.PromptTokens, stats.ReplyTokens) { Stats = stats };
    }

    /// <summary>Arguments come as an object; some models give them as a JSON string.</summary>
    private static JsonObject Arguments(JsonNode? node)
    {
        if (node is JsonObject obj)
            return (JsonObject)obj.DeepClone();
        if (node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0)
        {
            try { return JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { }
        }
        return new JsonObject();
    }
}
