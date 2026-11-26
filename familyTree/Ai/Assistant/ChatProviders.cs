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

/// <summary>
/// A tool the model may call: a name, what it does, and a JSON Schema for its arguments.  A deferred one is declared but
/// not shown to the model until a ToolsAddedTurn adds it (Anthropic).
/// </summary>
public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema, bool Deferred = false);

public sealed record ToolCall(string Id, string Name, JsonObject Arguments);

public sealed record ToolResult(string CallId, string Name, string Content, bool IsError);

/// <summary>The conversation, independent of the provider.</summary>
public abstract record ChatTurn;

/// <summary>
/// The person's message, with the context (tree, workspace, selected person) as it was when they sent it.  The context
/// goes with the message rather than in the system prompt, so the system prompt and every earlier turn stay the same
/// from step to step and the service can read them from its cache (TDD 22.2).  A model on this computer is sent the
/// turns without it and has it in its prompt instead (AssistantSession.ShortPrompt).
/// </summary>
public sealed record UserTurn(string Text, string? Context = null) : ChatTurn
{
    /// <summary>
    /// The message as the model is sent it: what the person wrote, then the context when there is one.  Put first, the
    /// context led qwen3:8b to give an empty reply to "Who are their descendants?" (26 November 2026, TDD 22.2).
    /// </summary>
    public string ForModel => Context is null ? Text : $"{Text}\n\n[From Mycelia, not the person: the context when this message was sent (JSON): {Context}]";
}

public sealed record AssistantTurn(string Text, IReadOnlyList<ToolCall> ToolCalls) : ChatTurn;

/// <summary>
/// Tools shown to Anthropic from this point of the conversation, when use_tools adds a group (TDD 22.2).  It is sent a
/// tool_addition for each, so the tools declared at the start, and the cache, stay the same.  Other providers are sent
/// every tool they are offered and skip this turn.
/// </summary>
public sealed record ToolsAddedTurn(IReadOnlyList<string> Names) : ChatTurn;

public sealed record ToolResultsTurn(IReadOnlyList<ToolResult> Results) : ChatTurn;

public sealed record ChatReply(string Text, IReadOnlyList<ToolCall> ToolCalls, string StopReason, int InputTokens, int OutputTokens)
{
    /// <summary>Token counts and times, where the service gives them (Ollama).</summary>
    public ReplyStats? Stats { get; init; }
}

/// <summary>
/// What one request cost: the tokens read and written, Ollama's own times for loading the model, reading the prompt and
/// writing the reply, and the time to the first text or tool call and in all, measured by Mycelia.
/// </summary>
public sealed record ReplyStats(int PromptTokens, int ReplyTokens, TimeSpan Loading, TimeSpan Reading, TimeSpan Writing,
    TimeSpan FirstOutput, TimeSpan Total)
{
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"prompt {PromptTokens:N0} tokens in {Reading.TotalSeconds:0.0} s, reply {ReplyTokens:N0} tokens in {Writing.TotalSeconds:0.0} s, "
        + $"model loaded in {Loading.TotalSeconds:0.0} s, first output after {FirstOutput.TotalSeconds:0.0} s, {Total.TotalSeconds:0.0} s in all");
}

public sealed class ChatException : Exception
{
    public ChatException(string message) : base(message)
    {
    }
}

public interface IChatProvider
{
    string DisplayName { get; }

    /// <summary>
    /// Sends the conversation and returns the reply.  A service that streams (Ollama) calls onText with each piece of text
    /// as it comes; the reply still holds the whole text.
    /// </summary>
    Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel,
        Action<string>? onText = null);
}

/// <summary>What a request needs.  ContextSize and Think are for Ollama only.</summary>
public sealed record ChatProviderOptions(ModelProvider Provider, string Model, string ApiKey, string Endpoint, int MaxTokens,
    int ContextSize = AssistantSettings.DefaultOllamaContext, bool Think = false);

/// <summary>The models a service lists, or none and why (ChatProviders.ListModelsAsync).</summary>
public sealed record ModelList(IReadOnlyList<string> Models, string? Problem);

/// <summary>The AI services the Assistant talks to.  Ported from multifariousStudio's Core/Assistant/ChatProviders.cs.</summary>
public static class ChatProviders
{
    public const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    public const string AnthropicVersion = "2023-06-01";

    /// <summary>Anthropic's beta for deferred tools and tool_addition, which keep the cache when tools are added.</summary>
    public const string ToolChangesBeta = "mid-conversation-tool-changes-2026-07-01";
    public const string OpenAiEndpoint = "https://api.openai.com/v1";

    /// <summary>
    /// The model saved when none is chosen: a name for Anthropic and OpenAI, none for another service.  The Model list in
    /// Options shows only what the service lists (ListModelsAsync).
    /// </summary>
    public static string DefaultModel(ModelProvider provider) => provider switch
    {
        ModelProvider.Anthropic => "claude-sonnet-5-5",
        ModelProvider.OpenAI => "gpt-5",
        _ => string.Empty
    };

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
        // Ollama on this computer: its own API, which sets the context size and thinking, and streams.
        _ when OllamaServer.IsOllamaEndpoint(options.Provider, options.Endpoint) => new OllamaChatProvider(options),
        _ => new OpenAiChatProvider(options)
    };

    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// The models a service on this computer has (Ollama, LM Studio: GET {base}/models), sorted; empty when it couldn't
    /// be reached within a few seconds or isn't on this computer.
    /// </summary>
    public static async Task<IReadOnlyList<string>> InstalledModelsAsync(ModelProvider provider, string endpoint, CancellationToken cancel = default) =>
        IsLocal(provider, endpoint) ? (await ListModelsAsync(provider, endpoint, string.Empty, cancel)).Models : [];

    /// <summary>
    /// The models the service lists for this key, for the Model list in Options (TDD 22.7): Anthropic's GET /v1/models
    /// newest first, OpenAI's GET /models newest first without the models that don't chat, and any other service's
    /// GET {base}/models sorted by name.  Empty, with the reason, when the list can't be read within a few seconds.
    /// </summary>
    public static async Task<ModelList> ListModelsAsync(ModelProvider provider, string endpoint, string apiKey, CancellationToken cancel = default)
    {
        apiKey = apiKey.Trim();
        if (NeedsKey(provider, endpoint) && apiKey.Length == 0)
            return new ModelList([], "Enter the API key to list this service's models.");
        if (ModelsUrl(provider, endpoint) is not { } url)
            return new ModelList([], "The address isn't a web address.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Without validation, so a key pasted with a stray character gives the service's refusal rather than an exception.
        if (provider == ModelProvider.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        }
        else if (apiKey.Length > 0)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await Http.SendAsync(request, timeout.Token);
            int status = (int)response.StatusCode;
            if (status is 401 or 403)
                return new ModelList([], "The service refused the API key, so it lists no models.");
            if (!response.IsSuccessStatusCode)
                return new ModelList([], $"The service answered {status} {response.ReasonPhrase} when asked for its models.");
            var models = ParseModels(await response.Content.ReadAsStringAsync(timeout.Token), provider);
            return new ModelList(models, models.Count == 0 ? "The service lists no models." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return new ModelList([], IsLocal(provider, endpoint)
                ? "The service on this computer isn't answering, so there are no models to list."
                : "The service couldn't be reached, so there are no models to list.");
        }
    }

    /// <summary>Where a service lists its models: beside the address its requests go to.</summary>
    private static Uri? ModelsUrl(ModelProvider provider, string endpoint)
    {
        string baseUrl = endpoint.Trim().TrimEnd('/');
        if (provider == ModelProvider.Anthropic)
        {
            if (baseUrl.Length == 0)
                baseUrl = AnthropicEndpoint;
            if (baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
                baseUrl = baseUrl[..^"/messages".Length];
            baseUrl += "/models?limit=1000";
        }
        else
        {
            if (baseUrl.Length == 0)
                baseUrl = provider == ModelProvider.OpenAI ? OpenAiEndpoint : string.Empty;
            if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                baseUrl = baseUrl[..^"/chat/completions".Length];
            baseUrl += "/models";
        }
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" ? url : null;
    }

    /// <summary>Parts of the names of OpenAI's models that don't answer chat requests (speech, images, embeddings and the like).</summary>
    private static readonly string[] NotForChat =
        ["embedding", "tts", "whisper", "dall-e", "moderation", "image", "audio", "realtime", "transcribe", "davinci", "babbage"];

    /// <summary>
    /// The ids in a model list ({"data":[{"id":"qwen3:8b"}]}): Anthropic's in its own order (newest first), OpenAI's newest
    /// first without the models that don't chat, and any other service's sorted by name.
    /// </summary>
    public static IReadOnlyList<string> ParseModels(string json, ModelProvider provider = ModelProvider.OpenAICompatible)
    {
        try
        {
            var entries = (JsonNode.Parse(json)?["data"] as JsonArray ?? new JsonArray())
                .Select(m => (Id: (m?["id"] as JsonValue)?.ToString() ?? string.Empty, Created: (m?["created"] as JsonValue)?.TryGetValue(out long c) == true ? c : 0))
                .Where(m => m.Id.Length > 0)
                .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase);
            return provider switch
            {
                ModelProvider.Anthropic => entries.Select(m => m.Id).ToList(),
                ModelProvider.OpenAI => entries.Where(m => !NotForChat.Any(part => m.Id.Contains(part, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(m => m.Created).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Select(m => m.Id).ToList(),
                _ => entries.Select(m => m.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Why a request can't go to the model on this computer, or null when it has it or the list is unknown.  Ollama takes
    /// a name without a tag as name:latest.
    /// </summary>
    public static string? MissingModel(string model, IReadOnlyList<string> installed)
    {
        if (installed.Count == 0)
            return null;
        if (model.Length > 0 && installed.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)
                || (!model.Contains(':') && m.Equals(model + ":latest", StringComparison.OrdinalIgnoreCase))))
            return null;
        string name = model.Length > 0 ? $"The model {model} isn't on this computer." : "No model is chosen.";
        return installed.Count == 1
            ? $"{name}  The service has {installed[0]}: choose it in Options › AI model."
            : $"{name}  The service has {string.Join(", ", installed)}: choose one in Options › AI model.";
    }

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
                throw Error(providerName, (int)response.StatusCode, body);
            return json ?? throw new ChatException($"{providerName} sent an answer that isn't JSON.");
        }
    }

    /// <summary>A service's refusal, with the message from its answer ({"error":{"message":...}} or {"error":"..."}).</summary>
    internal static ChatException Error(string providerName, int status, string body)
    {
        JsonObject? json = null;
        try { json = JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { }
        string message = ((json?["error"] as JsonObject)?["message"] as JsonValue)?.ToString()
            ?? (json?["error"] as JsonValue)?.ToString()
            ?? json?["error"]?.ToString()
            ?? (body.Length > 300 ? body[..300] + "…" : body);
        string hint = status == 401 ? "  Check the API key in Options › AI model." : string.Empty;
        return new ChatException($"{providerName} answered {status}: {message}{hint}");
    }
}

/// <summary>Anthropic Messages API with tool use (tool_use and tool_result content blocks).</summary>
public sealed class AnthropicChatProvider : IChatProvider
{
    private readonly ChatProviderOptions _options;

    public AnthropicChatProvider(ChatProviderOptions options) => _options = options;

    public string DisplayName => "Anthropic " + _options.Model;

    public async Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel,
        Action<string>? onText = null)
    {
        var messages = new JsonArray();
        var deferred = tools.Where(t => t.Deferred).Select(t => t.Name).ToHashSet();
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
                    Add("user", new JsonArray { new JsonObject { ["type"] = "text", ["text"] = user.ForModel } });
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
                // Only a tool deferred in this request can be added: a conversation kept from an earlier session, or begun
                // on another workspace, may name one that is now shown from the start or no longer there.
                case ToolsAddedTurn added when added.Names.Any(deferred.Contains):
                    Add("system", new JsonArray(added.Names.Where(deferred.Contains).Select(n => (JsonNode)new JsonObject
                    {
                        ["type"] = "tool_addition",
                        ["tool"] = new JsonObject { ["type"] = "tool_reference", ["name"] = n }
                    }).ToArray()));
                    break;
            }
        }

        // Prompt caching (TDD 22.2): the service reads the tools, then the system prompt, then the messages.  One mark at
        // the end of the system prompt keeps the tools and the prompt, which don't change within a conversation; one on
        // the last block of the last message keeps the conversation so far, so the next step reads it all from the cache.
        // The mark goes on the last message that isn't a tool_addition.  The system prompt's mark lasts an hour, so the
        // tools and prompt are read after a pause of more than five minutes rather than written again: it costs $4 a
        // million to write against $2.50, and halved the cost of four questions six minutes apart (26 November 2026).
        if (messages.LastOrDefault(m => m!["role"]!.GetValue<string>() != "system")?["content"] is JsonArray { Count: > 0 } last)
            last[^1]!["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
        var systemMark = new JsonObject { ["type"] = "ephemeral", ["ttl"] = "1h" };
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["system"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = system, ["cache_control"] = systemMark }
            },
            ["messages"] = messages
        };
        if (tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t =>
            {
                var tool = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = t.InputSchema.DeepClone()
                };
                if (t.Deferred)
                    tool["defer_loading"] = true;
                return (JsonNode)tool;
            }).ToArray());

        string url = string.IsNullOrWhiteSpace(_options.Endpoint) ? ChatProviders.AnthropicEndpoint : _options.Endpoint.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", ChatProviders.AnthropicVersion);
        if (deferred.Count > 0)
            request.Headers.Add("anthropic-beta", ChatProviders.ToolChangesBeta);

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

    public async Task<ChatReply> CompleteAsync(string system, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ToolSpec> tools, CancellationToken cancel,
        Action<string>? onText = null)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var turn in turns)
        {
            switch (turn)
            {
                case UserTurn user:
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = user.ForModel });
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
