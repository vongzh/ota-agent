using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Abstractions.Domain.Entities;

public sealed class HotelOrder
{
    public string OrderId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string PolicyId { get; set; } = "";
    public string PaymentId { get; set; } = "";
    public string? RefundId { get; set; }
    public string Status { get; set; } = "CONFIRMED";
    public int Version { get; set; } = 1;
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public string RoomType { get; set; } = "";
    public int RoomCount { get; set; } = 1;
    public decimal PaidAmount { get; set; }
    public string Currency { get; set; } = "CNY";
    public bool UserOnSite { get; set; }
    public string ChannelType { get; set; } = "DIRECT";
    public string ExtraJson { get; set; } = "{}";
}

public sealed class PolicySnapshot
{
    public string PolicyId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string RuleCode { get; set; } = "";
    public bool FreeCancel { get; set; }
    public decimal? FixedFee { get; set; }
    public string Authority { get; set; } = "platform";
    public string Tags { get; set; } = "";
}

public sealed class RefundCase
{
    public string CaseId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Status { get; set; } = "OPEN";
    public RiskLevel RiskLevel { get; set; }
    public string? Intent { get; set; }
    public string RecommendedAction { get; set; } = "";
    public decimal? QuoteRefundAmount { get; set; }
    public decimal? QuoteFeeAmount { get; set; }
    public string ConversationState { get; set; } = "START";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CaseEvent
{
    public long Id { get; set; }
    public string CaseId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkflowRun
{
    public string RunId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Status { get; set; } = "RUNNING";
    public string TraceJson { get; set; } = "[]";
    public string ToolSequenceJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ToolAuditLog
{
    public long Id { get; set; }
    public string TraceId { get; set; } = "";
    public string? CaseId { get; set; }
    public string ToolName { get; set; } = "";
    public ToolAccess Access { get; set; }
    public bool Allowed { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
    public string? DenyReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ScenarioFixture
{
    public string ScenarioId { get; set; } = "";
    public string Title { get; set; } = "";
    public string EntryMessage { get; set; } = "";
    public string UserId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public RiskLevel RiskLevel { get; set; }
    public string ExpectedRoute { get; set; } = "";
    public string ExpectedCaseStatus { get; set; } = "";
    public string RequiredToolsJson { get; set; } = "[]";
    public string ExpectedStatesJson { get; set; } = "[]";
    public string SuccessAssertionsJson { get; set; } = "[]";
    public string Group { get; set; } = "";
    public string Goal { get; set; } = "";
}
