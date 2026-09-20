namespace StayOta.Agent.Ai;

/// <summary>
/// Per-request turn configuration for <see cref="DeterministicRefundChatClient"/>.
/// Instance fields — no static AsyncLocal plan bus.
/// </summary>
public sealed class DeterministicTurnPlan
{
    public IReadOnlyList<string> HintTools { get; init; } = [];
    public string? SuggestedReply { get; init; }
    public string UserMessage { get; init; } = "";
    public string ConversationState { get; init; } = "";
    public string? PreferredWriteTool { get; init; }
    public bool AllowAutonomousToolSelection { get; init; } = true;
}

/// <summary>
/// Scoped holder so <see cref="DeterministicRefundChatClient"/> (scoped) reads the active turn plan.
/// </summary>
public sealed class DeterministicTurnContext
{
    public DeterministicTurnPlan? Plan { get; private set; }

    public void Set(DeterministicTurnPlan plan) => Plan = plan;

    public void Clear() => Plan = null;
}
