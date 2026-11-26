namespace Mycelia.Ai.Assistant;

/// <summary>
/// Fits a conversation into a model's context before it is sent (Ollama, whose context size is set in Options, TDD 22.2).
/// Old tool results go first, then whole old exchanges, oldest first; the system prompt, the tools and the current
/// request are never left out, though a result in the current request that is too large is cut.  Only what is sent is
/// trimmed: the conversation itself is kept whole.  Ported from HoboLibris.
/// </summary>
public static class ContextFit
{
    /// <summary>The most tokens kept free for the reply: a quarter of the context, up to this.</summary>
    public const int ReplyRoom = 2048;

    /// <summary>What a tool result is replaced with when it is left out.</summary>
    public const string LeftOut = "[Left out to fit the context.]";

    /// <summary>What ends a result of the current request that was cut to fit.</summary>
    public const string CutShort = "\n[Cut to fit the context. Ask for less at a time: a narrower search, fewer generations, or one person.]";

    /// <summary>The least of a result that is kept when it is cut.</summary>
    private const int KeptAtLeast = 600;

    // Characters per token: Mycelia's 68 tool definitions measured 4.5 with qwen3:8b on 25 November 2026, and
    // HoboLibris measured 4.3 for a system prompt and 3.2 for a search result in JSON.  Each is rounded down, so the
    // estimate errs on the large side.
    private const double PromptChars = 4.0;
    private const double ToolChars = 4.3;
    private const double TurnChars = 3.0;

    /// <summary>The tokens for the conversation: the context size less room for the reply, a quarter of it up to ReplyRoom.</summary>
    public static int Budget(int contextSize) => contextSize - Math.Min(ReplyRoom, contextSize / 4);

    /// <summary>An estimate of the tokens the system prompt and the tool definitions take.</summary>
    public static int Fixed(string system, IReadOnlyList<ToolSpec> tools) =>
        (int)(system.Length / PromptChars) + (int)(tools.Sum(t => t.Name.Length + t.Description.Length + t.InputSchema.ToJsonString().Length) / ToolChars);

    /// <summary>An estimate of the tokens one turn takes, with a few for the message's own marks.</summary>
    public static int Estimate(ChatTurn turn) => turn switch
    {
        UserTurn user => 4 + (int)(user.ForModel.Length / TurnChars),
        AssistantTurn assistant => 4 + (int)((assistant.Text.Length
            + assistant.ToolCalls.Sum(c => c.Name.Length + c.Arguments.ToJsonString().Length + 12)) / TurnChars),
        ToolResultsTurn results => results.Results.Sum(r => 6 + (int)((r.Name.Length + r.Content.Length) / TurnChars)),
        _ => 0
    };

    /// <summary>
    /// The turns to send so that they and the fixed part fit the budget, and whether anything was left out.  When even the
    /// current request doesn't fit, what is left is sent and the service refuses it.
    /// </summary>
    public static IReadOnlyList<ChatTurn> Fit(IReadOnlyList<ChatTurn> turns, int fixedTokens, int budget, out bool trimmed)
    {
        trimmed = false;
        var sent = turns.ToList();
        int total = fixedTokens + sent.Sum(Estimate);
        if (total <= budget)
            return sent;
        // The current request: the person's last message and what has followed it.
        int current = sent.FindLastIndex(t => t is UserTurn);
        if (current < 0)
            return sent;

        // Old tool results first, oldest first: they are the largest part and the least needed later.
        for (int i = 0; i < current && total > budget; i++)
        {
            if (sent[i] is ToolResultsTurn results && results.Results.Any(r => r.Content != LeftOut))
            {
                var shorter = new ToolResultsTurn(results.Results.Select(r => r with { Content = LeftOut }).ToList());
                total += Estimate(shorter) - Estimate(results);
                sent[i] = shorter;
                trimmed = true;
            }
        }

        // Then whole exchanges, oldest first: a message from the person and the replies and tool results after it.
        while (total > budget && current > 0)
        {
            int end = sent.FindIndex(1, t => t is UserTurn);
            if (end <= 0 || end > current)
                break;
            total -= sent.Take(end).Sum(Estimate);
            sent.RemoveRange(0, end);
            current -= end;
            trimmed = true;
        }

        // Last, the results in the current request, largest first: they are cut, not left out.
        while (total > budget)
        {
            var largest = sent.Select((t, i) => (Turn: t as ToolResultsTurn, Index: i))
                .Where(x => x.Index > current && x.Turn != null)
                .SelectMany(x => x.Turn!.Results.Select((r, j) => (x.Index, Result: j, r.Content.Length)))
                .Where(x => x.Length > KeptAtLeast + CutShort.Length)
                .OrderByDescending(x => x.Length)
                .FirstOrDefault();
            if (largest.Length == 0)
                break;
            var turn = (ToolResultsTurn)sent[largest.Index];
            var result = turn.Results[largest.Result];
            int keep = Math.Max(KeptAtLeast, result.Content.Length - (int)((total - budget) * TurnChars) - CutShort.Length);
            var cut = result with { Content = result.Content[..keep] + CutShort };
            var shorter = new ToolResultsTurn(turn.Results.Select((r, j) => j == largest.Result ? cut : r).ToList());
            total += Estimate(shorter) - Estimate(turn);
            sent[largest.Index] = shorter;
            trimmed = true;
        }
        return sent;
    }
}
