using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mycelia.Ai.Assistant;

/// <summary>One line of the Assistant pane.  <see cref="Changed"/> fires when a running item moves on (a tool finishing, an approval answered).</summary>
public abstract class ChatItem
{
    // Set when an item is restored from a saved conversation.
    public DateTime Time { get; init; } = DateTime.Now;

    public event Action? Changed;

    protected void RaiseChanged() => Changed?.Invoke();

    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

public sealed class UserMessage(string text, string context) : ChatItem
{
    public string Text { get; } = text;

    /// <summary>Where the person was when they asked ("People · Arthur Lee"), shown under the message.</summary>
    public string Context { get; } = context;
}

public sealed class AssistantMessage(string text) : ChatItem
{
    public string Text { get; } = text;
}

public sealed class NoticeMessage(string text, bool isError) : ChatItem
{
    public string Text { get; } = text;
    public bool IsError { get; } = isError;
}

public enum ToolActivityState
{
    Running,
    Done,
    Failed,
    Declined
}

/// <summary>A tool the assistant used: what it was, its arguments, and what came back.</summary>
public sealed class ToolActivity : ChatItem
{
    private ToolActivityState _state = ToolActivityState.Running;
    private string _summary = "Running…";
    private string _result = string.Empty;

    /// <param name="source">Who it is from, when not the tool's own source (another program calling it, TDD 24).</param>
    public ToolActivity(AssistantTool tool, JsonObject arguments, string? source = null)
        : this(tool.Title, source ?? tool.Source, arguments.ToJsonString(Indented))
    {
    }

    /// <summary>A tool activity as saved (a restored conversation).</summary>
    public ToolActivity(string title, string source, string arguments)
    {
        Title = title;
        Source = source;
        Arguments = arguments;
    }

    public string Title { get; }
    public string Source { get; }
    public string Arguments { get; }

    public ToolActivityState State { get => _state; set { _state = value; RaiseChanged(); } }
    public string Summary { get => _summary; set { _summary = value; RaiseChanged(); } }
    public string Result { get => _result; set { _result = value; RaiseChanged(); } }
}

/// <summary>
/// The end of a request that changed the tree: how many changes, and Revert, which puts the tree back as it was before
/// them (one undo step) while nothing has changed since.  A line restored from a saved conversation can't revert.
/// </summary>
public sealed class ChangesMade(int count, Func<string>? revert, string? outcome = null) : ChatItem
{
    private Func<string>? _revert = revert;

    public int Count { get; } = count;
    public string? Outcome { get; private set; } = outcome;
    public bool CanRevert => _revert is not null && Outcome is null;

    public void Revert()
    {
        if (_revert is null || Outcome is not null) return;
        Outcome = _revert();
        _revert = null;
        RaiseChanged();
    }
}

/// <summary>The assistant wants to use a tool set to Ask: the person allows it once, always, or declines.</summary>
public sealed class ApprovalRequest : ChatItem
{
    private readonly TaskCompletionSource<(bool Allowed, bool Always)> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="source">Who is asking, when not the Assistant (another program, TDD 24); shown on the card.</param>
    public ApprovalRequest(AssistantTool tool, JsonObject arguments, string? source = null)
        : this(tool.Title, source ?? tool.Source, (tool.Describe?.Invoke(arguments) ?? $"Use {tool.Title} from {tool.Source}."), arguments.ToJsonString(Indented))
    {
        AlwaysLabel = tool.AlwaysLabel;
        AlwaysOutcome = tool.AlwaysOutcome;
    }

    /// <summary>The "always" button's words, and the outcome it leaves (fetch_url: this site only).</summary>
    public string AlwaysLabel { get; } = "Always allow";
    public string AlwaysOutcome { get; } = "Allowed (always for this tool)";

    /// <summary>The other program asking ("Claude Desktop"), or null for the Assistant itself.</summary>
    public string? FromProgram { get; init; }

    private ApprovalRequest(string title, string source, string description, string arguments)
    {
        Title = title;
        Source = source;
        Description = description;
        Arguments = arguments;
    }

    /// <summary>An approval as saved (a restored conversation): already answered, with its outcome.</summary>
    public static ApprovalRequest Restored(string title, string source, string description, string arguments, string outcome)
    {
        var approval = new ApprovalRequest(title, source, description, arguments);
        approval._answer.TrySetResult((outcome != "Declined", false));
        approval.Outcome = outcome;
        return approval;
    }

    public string Title { get; }
    public string Source { get; }
    public string Description { get; }
    public string Arguments { get; }
    public bool IsPending => !_answer.Task.IsCompleted;
    public string Outcome { get; private set; } = string.Empty;
    public Task<(bool Allowed, bool Always)> Answered => _answer.Task;

    public void Answer(bool allowed, bool always)
    {
        if (!_answer.TrySetResult((allowed, always)))
            return;
        Outcome = !allowed ? "Declined" : always ? AlwaysOutcome : "Allowed";
        RaiseChanged();
    }
}
