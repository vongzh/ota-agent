using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;

namespace StayOta.Agent.Abstractions.Contracts;

public sealed record AgentMessageRequest(
    string Message,
    string? ScenarioId = null,
    string? UserId = null,
    bool HasEvidence = false,
    bool HasNegotiationReason = false,
    bool LowConfidence = false,
    bool ServiceError = false,
    bool ConfirmWrite = false,
    string? ConfirmationToken = null,
    string? IdempotencyKey = null,
    bool ResetDemo = false,
    /// <summary>Resume ChatClientAgent session across turns when set.</summary>
    string? AgentSessionId = null);

public sealed record DecisionStepDto(string Step, string Status, string Detail, double? Score = null);

public sealed record PolicyMatchDto(string PolicyId, string Title, double Score, string Summary);

public sealed record TicketLifecycleStepDto(string Stage, string Status, string Detail);

public sealed record TicketDto(
    string TicketId,
    string Priority,
    string Queue,
    string Summary,
    IReadOnlyList<string> Facts,
    IReadOnlyList<TicketLifecycleStepDto> Lifecycle);

public sealed record HitlStateDto(
    bool RequiresConfirmation,
    string? PendingAction,
    string? ConfirmationToken,
    /// <summary>Unified HITL gate: FunctionApproval is the sole user-facing approval surface; confirmation_token remains the Gateway write credential.</summary>
    string Gate = "FunctionApproval + confirmation_token + expected_order_version + idempotency");

public sealed record ToolAuditDto(
    long Id,
    string TraceId,
    string? CaseId,
    string ToolName,
    string Access,
    bool Allowed,
    string? DenyReason,
    DateTimeOffset CreatedAt);

public sealed record AgentSessionDetailDto(
    string SessionId,
    string TraceId,
    string UserId,
    string OrderId,
    string CaseId,
    string ScenarioId,
    string ConversationState,
    int PendingApprovalCount,
    IReadOnlyList<PendingApprovalDto> PendingApprovals,
    DateTimeOffset? UpdatedAt);

public sealed record PendingApprovalDto(
    string RequestId,
    string CallId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    string Description);

public sealed record FunctionApprovalRequest(
    string SessionId,
    string RequestId,
    bool Approved,
    string? Reason = null);

public sealed record AgentDecisionDto(
    string TraceId,
    string RunId,
    string CaseId,
    string ScenarioId,
    string Intent,
    double IntentConfidence,
    RiskLevel RiskLevel,
    int RiskScore,
    string Action,
    string Conclusion,
    string PlanTitle,
    string PlanCopy,
    decimal? RefundAmount,
    decimal? FeeAmount,
    string Reply,
    string ConversationState,
    string CaseStatus,
    IReadOnlyList<DecisionStepDto> Steps,
    IReadOnlyDictionary<string, string> Slots,
    IReadOnlyList<PolicyMatchDto> PolicyMatches,
    IReadOnlyList<string> ToolSequence,
    TicketDto? Ticket,
    HotelOrderDto Order,
    bool VerificationPassed,
    IReadOnlyList<string> VerificationViolations,
    HitlStateDto? Hitl = null,
    string? AiProvider = null,
    string? AgentSessionId = null,
    bool AgentDriven = false,
    bool HasPendingApprovals = false,
    IReadOnlyList<PendingApprovalDto>? PendingApprovals = null,
    string? ProductionMode = null);

public interface IAgentOrchestrator
{
    Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default);
    Task<AgentDecisionDto> RespondToApprovalAsync(FunctionApprovalRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AgentStreamEvent> HandleStreamAsync(AgentMessageRequest request, CancellationToken ct = default);
}

/// <summary>SSE / progressive agent events for streaming UX.</summary>
public sealed record AgentStreamEvent(string Type, string? Text = null, object? Data = null);

public sealed record HotelOrderDto(
    string OrderId,
    string HotelName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    decimal Amount,
    string Currency,
    string Status,
    bool UserOnSite,
    string PolicyId,
    int Version,
    string RoomType,
    int RoomCount);

public sealed record ScenarioDto(
    string ScenarioId,
    string Title,
    string Group,
    string Goal,
    string EntryMessage,
    RiskLevel RiskLevel,
    string ExpectedRoute,
    IReadOnlyList<string> RequiredTools);

public sealed record EvalCaseDto(
    string Id,
    string Message,
    string ExpectedScenario,
    string RiskLevel,
    IReadOnlyList<string>? ExpectedToolsSubsequence = null,
    IReadOnlyList<string>? ForbiddenReplySubstrings = null,
    string? ExpectedAction = null,
    decimal? MinRefundAmount = null,
    decimal? MaxFeeAmount = null);

public sealed record EvalResultDto(string Id, string Message, string ExpectedScenario, string ActualScenario, bool Passed, string? Detail);

public sealed record ConfirmActionRequest(string CaseId, string OrderId, int OrderVersion, string Action, string IdempotencyKey);
public sealed record ConfirmActionResponse(bool Success, string Message, string? ConfirmationToken = null);

public interface IScenarioCatalog
{
    IReadOnlyList<ScenarioDto> List();
    ScenarioFixture GetRequired(string scenarioId);
}

public interface IEvalRunner
{
    Task<IReadOnlyList<EvalResultDto>> RunAllAsync(CancellationToken ct = default);
    IReadOnlyList<EvalCaseDto> ListCases();
}

public interface IIntentService
{
    (string Intent, string Reason, double Confidence, Dictionary<string, string> Slots) Analyze(string message, ScenarioFixture scenario, bool lowConfidence);
}

public interface IPolicyRetrieval
{
    IReadOnlyList<PolicyMatchDto> Retrieve(HotelOrder order, PolicySnapshot policy, string reason);
}

public interface IRulesEngine
{
    RuleDecision Evaluate(HotelOrder order, PolicySnapshot policy, ScenarioFixture scenario, AgentSignals signals);
}

public sealed record AgentSignals(bool HasEvidence, bool HasNegotiationReason, bool LowConfidence, bool ServiceError, bool ConfirmWrite);

public sealed record RuleDecision(
    string Action,
    RiskLevel RiskLevel,
    int RiskScore,
    decimal RefundAmount,
    decimal FeeAmount,
    string Conclusion,
    string PlanTitle,
    string PlanCopy,
    string RuleCode,
    bool NeedsUserConfirm,
    bool NeedsEvidence,
    string ConversationState,
    string CaseStatus);

public interface IToolGateway
{
    Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default);
    IReadOnlyList<ToolContractDto> ListContracts();
}

public sealed record ToolCall(
    string TraceId,
    string ToolName,
    ToolAccess Access,
    string UserId,
    string? OrderId,
    string? CaseId,
    RiskLevel RiskLevel,
    string ConversationState,
    IDictionary<string, object?> Arguments,
    string? ConfirmationToken = null,
    string? IdempotencyKey = null,
    int? ExpectedOrderVersion = null);

public sealed record ToolResult(bool Allowed, bool Success, string ToolName, object? Data, string? DenyReason = null);

public interface IConfirmationStore
{
    Task<string> IssueAsync(string caseId, string orderId, int version, string action, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> ConsumeAsync(string token, string caseId, string orderId, int version, string action, CancellationToken ct = default);
}

public interface IIdempotencyStore
{
    /// <summary>Reserve the idempotency key for an in-flight write. Returns false if already reserved or completed.</summary>
    Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Persist the successful write payload so duplicates can replay the same business result.</summary>
    Task CompleteAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Return completed response JSON, or null if missing / still in-flight.</summary>
    Task<string?> TryGetCompletedAsync(string key, CancellationToken ct = default);

    /// <summary>Release a begun key after a failed attempt so a retry can re-acquire.</summary>
    Task AbandonAsync(string key, CancellationToken ct = default);
}

public interface ISessionStore
{
    Task SetAsync(string sessionId, string json, TimeSpan ttl, CancellationToken ct = default);
    Task<string?> GetAsync(string sessionId, CancellationToken ct = default);
}
