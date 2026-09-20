using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Abstractions.Ai;

public interface IAgentConversationService
{
    Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct = default);
    Task<AgentTurnResult> RespondToApprovalAsync(ApprovalResponseRequest request, CancellationToken ct = default);
}

public sealed record AgentTurnRequest(
    string Message,
    string TraceId,
    string UserId,
    string OrderId,
    string CaseId,
    string ScenarioId,
    RiskLevel RiskLevel,
    string ConversationState,
    /// <summary>Soft tool hints (e.g. scenario required tools). Executed by Agent, not Orchestrator.</summary>
    IReadOnlyList<string> HintTools,
    string SuggestedReply,
    bool RequireWriteApproval,
    string? WriteToolName,
    IDictionary<string, object?> AmbientArguments,
    string? ConfirmationToken = null,
    string? IdempotencyKey = null,
    int? ExpectedOrderVersion = null,
    string? ExistingSessionId = null,
    bool AllowAutonomousToolSelection = true);

public sealed record ApprovalResponseRequest(
    string SessionId,
    string RequestId,
    bool Approved,
    string? Reason = null);

public sealed record PendingToolApprovalDto(
    string RequestId,
    string CallId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    string Description);

public sealed record AgentTurnResult(
    string SessionId,
    string Reply,
    bool HasPendingApprovals,
    IReadOnlyList<PendingToolApprovalDto> PendingApprovals,
    IReadOnlyList<string> ToolsInvoked,
    bool AgentDriven);

public interface IAgentSessionStore
{
    Task SaveAsync(string sessionId, AgentSessionSnapshot snapshot, CancellationToken ct = default);
    Task<AgentSessionSnapshot?> GetAsync(string sessionId, CancellationToken ct = default);
}

public sealed class AgentSessionSnapshot
{
    public string SessionJson { get; set; } = "";
    public string TraceId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string RiskLevel { get; set; } = "L1";
    public string ConversationState { get; set; } = "";
    public string? ConfirmationToken { get; set; }
    public string? IdempotencyKey { get; set; }
    public int? ExpectedOrderVersion { get; set; }
    public Dictionary<string, object?> AmbientArguments { get; set; } = new();
    public List<PendingApprovalRecord> PendingApprovals { get; set; } = [];
}

public sealed class PendingApprovalRecord
{
    public string RequestId { get; set; } = "";
    public string CallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

/// <summary>
/// Drives <see cref="ChatClientAgent"/> for dialogue, FunctionApproval, and tool calling.
/// </summary>
