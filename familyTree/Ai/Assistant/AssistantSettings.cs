using Mycelia.Ai.Mcp;

namespace Mycelia.Ai.Assistant;

/// <summary>How much about a living person the Assistant's tools give the AI model (TDD 22.4).</summary>
public enum LivingPeopleDetail
{
    /// <summary>Id, name, sex and links; dates, places, facts, notes and media withheld (the default).</summary>
    NamesOnly,
    /// <summary>Everything, as for anyone else.</summary>
    Full,
    /// <summary>Only an id and "Living person".</summary>
    None
}

/// <summary>Options › AI model and Assistant, kept in settings.json (TDD 22.7).</summary>
public sealed class AssistantSettings
{
    public ModelProvider Provider { get; set; } = ModelProvider.Anthropic;
    public string Model { get; set; } = ChatProviders.DefaultModel(ModelProvider.Anthropic);

    /// <summary>The base address of an OpenAI-compatible service (or a different Anthropic or OpenAI address).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>API keys by provider name, encrypted with DPAPI (SecretStore).</summary>
    public Dictionary<string, string> ApiKeys { get; set; } = new();

    /// <summary>
    /// The most tokens in one reply (256 to 64000).  16000 as in multifariousStudio: reasoning models such as GPT-5
    /// count their reasoning in it.
    /// </summary>
    public const int DefaultMaxTokens = 16000;

    public int MaxTokens { get; set; } = DefaultMaxTokens;

    /// <summary>The most model turns (each may call tools) for one request.</summary>
    public int MaxSteps { get; set; } = 20;

    /// <summary>Extra instructions added to every conversation.</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Connect to the MCP servers when the app starts.</summary>
    public bool ConnectOnStart { get; set; } = true;

    /// <summary>Never ask before a tool runs (every Ask counts as Allow).  Off by default.</summary>
    public bool SkipApprovals { get; set; }

    /// <summary>What the tools give the model about living people (Living.IsLiving).</summary>
    public LivingPeopleDetail LivingPeople { get; set; } = LivingPeopleDetail.NamesOnly;

    /// <summary>People and facts marked Private go to the model as anyone else's; off by default (withheld as living people are).</summary>
    public bool IncludePrivate { get; set; }

    /// <summary>Policies for the built-in tools that differ from their defaults.</summary>
    public Dictionary<string, ToolPolicy> BuiltInPolicies { get; set; } = new();

    /// <summary>The Assistant pane on the right of every workspace: shown or not, and its width.</summary>
    public bool PaneVisible { get; set; }
    public double PaneWidth { get; set; } = 400;

    /// <summary>The pane folded to a strip on the right edge (FoldPanel).</summary>
    public bool PaneFolded { get; set; }

    /// <summary>
    /// Sites fetch_url may read without asking, besides the listed archive APIs: each one the person chose "Always allow
    /// this site" for, as its origin ("https://www.freecen.org.uk").  Options › Research lists them (TDD 25.4).
    /// </summary>
    public List<string> AllowedSites { get; set; } = new();

    /// <summary>The address's site is one the person always allows (scheme, host and port must match).</summary>
    public bool SiteAllowed(Uri uri) => AllowedSites.Contains(SiteOf(uri), StringComparer.OrdinalIgnoreCase);

    /// <summary>The site of an address: "https://www.freecen.org.uk", with the port when it isn't the usual one.</summary>
    public static string SiteOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    /// <summary>Keys for the archive APIs fetch_url adds itself (Options › Research, TDD 25.4), by API id, encrypted with DPAPI.</summary>
    public Dictionary<string, string> ResearchKeys { get; set; } = new();

    public string GetResearchKey(string api) =>
        ResearchKeys.TryGetValue(api, out var stored) ? SecretStore.Unprotect(stored) : string.Empty;

    public void SetResearchKey(string api, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            ResearchKeys.Remove(api);
        else
            ResearchKeys[api] = SecretStore.Protect(key.Trim());
    }

    public string GetApiKey(ModelProvider provider) =>
        ApiKeys.TryGetValue(provider.ToString(), out var stored) ? SecretStore.Unprotect(stored) : string.Empty;

    public void SetApiKey(ModelProvider provider, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            ApiKeys.Remove(provider.ToString());
        else
            ApiKeys[provider.ToString()] = SecretStore.Protect(key.Trim());
    }

    public bool HasKey => !ChatProviders.NeedsKey(Provider, Endpoint) || GetApiKey(Provider).Length > 0;

    public string ProviderName => Provider switch
    {
        ModelProvider.Anthropic => "Anthropic",
        ModelProvider.OpenAI => "OpenAI",
        _ => ChatProviders.IsLocal(Provider, Endpoint) ? "Local AI service" : "AI service"
    };

    public string ModelLine => $"{ProviderName} · {Model}";
}
