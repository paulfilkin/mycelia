using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Assistant;

public enum ModelProvider
{
    Anthropic,
    OpenAI,
    /// <summary>Any service with the OpenAI chat completions interface: Gemini, Mistral, Ollama, LM Studio...</summary>
    OpenAICompatible
}

/// <summary>A tool the model may call: a name, what it does, and a JSON Schema for its arguments.</summary>
public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema);

public sealed record ToolCall(string Id, string Name, JsonObject Arguments);

public sealed record ToolResult(string CallId, string Name, string Content, bool IsError);

/// <summary>The conversation, independent of the provider.</summary>
public abstract record ChatTurn;

public sealed record UserTurn(string Text) : ChatTurn;

public sealed record AssistantTurn(string Text, IReadOnlyList<ToolCall> ToolCalls) : ChatTurn;

public sealed record ToolResultsTurn(IReadOnlyList<ToolResult> Results) : ChatTurn;

public sealed record ChatReply(string Text, IReadOnlyList<ToolCall> ToolCalls, string StopReason, int InputTokens, int OutputTokens);

public sealed class ChatException : Exception
{
    public ChatException(string message) : base(message)
    {
    }
}

public interface IChatProvider
{
    string DisplayName { get; }
    Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel);
}

public sealed record ChatProviderOptions(ModelProvider Provider, string Model, string ApiKey, string Endpoint, int MaxTokens);

/// <summary>The AI services the Assistant talks to.  Ported from multifariousStudio's Core/Assistant/ChatProviders.cs.</summary>
public static class ChatProviders
{
    public const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    public const string AnthropicVersion = "2023-06-01";
    public const string OpenAiEndpoint = "https://api.openai.com/v1";

    /// <summary>Suggested model names per provider (any name can be typed).</summary>
    public static IReadOnlyList<string> SuggestedModels(ModelProvider provider) => provider switch
    {
        ModelProvider.Anthropic => new[] { "claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5", "claude-fable-5-2" },
        ModelProvider.OpenAI => new[] { "gpt-5", "gpt-5-mini", "gpt-4.1", "gpt-4o" },
        _ => new[] { "llama3.1", "qwen2.5", "gemini-2.5-pro", "gemini-2.5-flash", "mistral-large-latest" }
    };

    public static string DefaultModel(ModelProvider provider) => SuggestedModels(provider)[0];

    /// <summary>Base addresses offered for OpenAI-compatible services.</summary>
    public static IReadOnlyList<string> SuggestedEndpoints { get; } = new[]
    {
        "http://localhost:11434/v1",
        "http://localhost:1234/v1",
        "https://generativelanguage.googleapis.com/v1beta/openai",
        "https://api.mistral.ai/v1"
    };

    public static bool NeedsKey(ModelProvider provider, string endpoint) =>
        provider != ModelProvider.OpenAICompatible || !(endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase) || endpoint.Contains("127.0.0.1"));

    /// <summary>Whether requests stay on this computer (a local OpenAI-compatible service such as Ollama).</summary>
    public static bool IsLocal(ModelProvider provider, string endpoint) => !NeedsKey(provider, endpoint);

    public static IChatProvider Create(ChatProviderOptions options) => options.Provider switch
    {
        ModelProvider.Anthropic => new AnthropicChatProvider(options),
        _ => new OpenAiChatProvider(options)
    };

    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    internal static async Task<JsonObject> PostAsync(HttpRequestMessage request, string providerName, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatException($"{providerName} couldn't be reached: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ChatException($"{providerName} didn't answer within five minutes.");
        }
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancel);
            JsonObject? json = null;
            try { json = JsonNode.Parse(body) as JsonObject; }
            catch (JsonException) { }
            if (!response.IsSuccessStatusCode)
            {
                string message = json?["error"]?["message"]?.GetValue<string>()
                    ?? json?["error"]?.ToString()
                    ?? (body.Length > 300 ? body[..300] + "…" : body);
                string hint = (int)response.StatusCode == 401 ? "  Check the API key in Options › AI model." : string.Empty;
                throw new ChatException($"{providerName} answered {(int)response.StatusCode}: {message}{hint}");
            }
            return json ?? throw new ChatException($"{providerName} sent an answer that isn't JSON.");
        }
    }
}

/// <summary>Anthropic Messages API with tool use (tool_use and tool_result content blocks).</summary>
public sealed class AnthropicChatProvider : IChatProvider
{
    private readonly ChatProviderOptions _options;

    public AnthropicChatProvider(ChatProviderOptions options) => _options = options;

    public string DisplayName => "Anthropic " + _options.Model;

    public async Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel)
    {
        var messages = new JsonArray();
        void Add(string role, JsonArray content)
        {
            // Consecutive turns of one role are merged into one message.
            if (messages.Count > 0 && messages[^1]!["role"]!.GetValue<string>() == role)
            {
                var existing = (JsonArray)messages[^1]!["content"]!;
                foreach (var block in content.ToList())
                {
                    content.Remove(block);
                    existing.Add(block);
                }
                return;
            }
            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        foreach (var turn in turns)
        {
            switch (turn)
            {
                case UserTurn user:
                    Add("user", new JsonArray { new JsonObject { ["type"] = "text", ["text"] = user.Text } });
                    break;
                case AssistantTurn assistant:
                    var blocks = new JsonArray();
                    if (assistant.Text.Length > 0)
                        blocks.Add(new JsonObject { ["type"] = "text", ["text"] = assistant.Text });
                    foreach (var call in assistant.ToolCalls)
                        blocks.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = call.Arguments.DeepClone() });
                    if (blocks.Count > 0)
                        Add("assistant", blocks);
                    break;
                case ToolResultsTurn results:
                    var resultBlocks = new JsonArray();
                    foreach (var result in results.Results)
                        resultBlocks.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = result.CallId,
                            ["content"] = result.Content.Length == 0 ? "(no output)" : result.Content,
                            ["is_error"] = result.IsError
                        });
                    Add("user", resultBlocks);
                    break;
            }
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["system"] = system,
            ["messages"] = messages
        };
        if (tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = t.InputSchema.DeepClone()
            }).ToArray());

        string url = string.IsNullOrWhiteSpace(_options.Endpoint) ? ChatProviders.AnthropicEndpoint : _options.Endpoint.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", ChatProviders.AnthropicVersion);

        var json = await ChatProviders.PostAsync(request, "Anthropic", cancel);
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        if (json["content"] is JsonArray content)
        {
            foreach (var block in content.OfType<JsonObject>())
            {
                switch (block["type"]?.GetValue<string>())
                {
                    case "text":
                        text.Append(block["text"]?.GetValue<string>());
                        break;
                    case "tool_use":
                        calls.Add(new ToolCall(block["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                            block["name"]?.GetValue<string>() ?? string.Empty,
                            block["input"]?.DeepClone() as JsonObject ?? new JsonObject()));
                        break;
                }
            }
        }
        return new ChatReply(text.ToString(), calls, json["stop_reason"]?.GetValue<string>() ?? string.Empty,
            json["usage"]?["input_tokens"]?.GetValue<int>() ?? 0, json["usage"]?["output_tokens"]?.GetValue<int>() ?? 0);
    }
}

/// <summary>OpenAI chat completions with function tools; also any compatible service.</summary>
public sealed class OpenAiChatProvider : IChatProvider
{
    private readonly ChatProviderOptions _options;

    public OpenAiChatProvider(ChatProviderOptions options) => _options = options;

    private string ProviderName => _options.Provider == ModelProvider.OpenAI ? "OpenAI" : "The AI service";

    public string DisplayName => (_options.Provider == ModelProvider.OpenAI ? "OpenAI " : string.Empty) + _options.Model;

    public async Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel)
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
                    // A reply with neither text nor tool calls (one cut short by the token limit) is refused by the API
                    // ("content: expected a string, got null"), and it would block every later request: leave it out,
                    // as the Anthropic side does.
                    if (assistant.Text.Length == 0 && assistant.ToolCalls.Count == 0)
                        break;
                    var message = new JsonObject { ["role"] = "assistant", ["content"] = assistant.Text.Length > 0 ? assistant.Text : null };
                    if (assistant.ToolCalls.Count > 0)
                        message["tool_calls"] = new JsonArray(assistant.ToolCalls.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.ToJsonString() }
                        }).ToArray());
                    messages.Add(message);
                    break;
                case ToolResultsTurn results:
                    foreach (var result in results.Results)
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = result.CallId,
                            ["content"] = (result.IsError ? "ERROR: " : string.Empty) + (result.Content.Length == 0 ? "(no output)" : result.Content)
                        });
                    break;
            }
        }

        var body = new JsonObject { ["model"] = _options.Model, ["messages"] = messages };
        // OpenAI's newer models take max_completion_tokens; compatible services still read max_tokens.
        body[_options.Provider == ModelProvider.OpenAI ? "max_completion_tokens" : "max_tokens"] = _options.MaxTokens;
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

        string baseUrl = string.IsNullOrWhiteSpace(_options.Endpoint) ? ChatProviders.OpenAiEndpoint : _options.Endpoint.Trim().TrimEnd('/');
        string url = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (_options.ApiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var json = await ChatProviders.PostAsync(request, ProviderName, cancel);
        var choice = (json["choices"] as JsonArray)?.FirstOrDefault() as JsonObject
            ?? throw new ChatException($"{ProviderName} sent no answer.");
        var reply = choice["message"] as JsonObject ?? new JsonObject();
        var calls = new List<ToolCall>();
        if (reply["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls.OfType<JsonObject>())
            {
                string arguments = call["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                JsonObject parsed;
                try { parsed = JsonNode.Parse(arguments.Length == 0 ? "{}" : arguments) as JsonObject ?? new JsonObject(); }
                catch (JsonException) { parsed = new JsonObject(); }
                calls.Add(new ToolCall(call["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    call["function"]?["name"]?.GetValue<string>() ?? string.Empty, parsed));
            }
        }
        return new ChatReply(reply["content"]?.GetValue<string>() ?? string.Empty, calls,
            choice["finish_reason"]?.GetValue<string>() ?? string.Empty,
            json["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0, json["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0);
    }
}
