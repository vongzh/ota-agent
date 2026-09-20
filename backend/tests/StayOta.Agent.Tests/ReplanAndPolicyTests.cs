using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Plugins.Refund.Services;
using Xunit;

namespace StayOta.Agent.Tests;

public class ToolFailureReplannerTests
{
    [Fact]
    public void L3Denial_EscalatesToHandoff()
    {
        var suggestions = ToolFailureReplanner.Suggest(
            "submit_cancellation", "L3 blocks auto financial write; escalate", "ConfirmCancel", RiskLevel.L3);
        Assert.Contains(suggestions, s => s.ToolName == "create_human_handoff");
    }

    [Fact]
    public void WrongState_RetriesWithAlternateState()
    {
        var suggestions = ToolFailureReplanner.Suggest(
            "get_order_detail", "tool get_order_detail not allowed in state START",
            "ConfirmCancel", RiskLevel.L1);
        Assert.Contains(suggestions, s => s.ToolName == "get_order_detail" && s.ConversationState == "FACTS_REQUIRED");
        Assert.Contains(suggestions, s => s.ToolName == "get_order_detail" && s.Reason.Contains("状态"));
    }

    [Fact]
    public void ProgressFailure_SuggestsRefundStatus()
    {
        var suggestions = ToolFailureReplanner.Suggest(
            "schedule_deadline_action", "temporary failure", "ExplainProgress", RiskLevel.L1);
        Assert.Contains(suggestions, s => s.ToolName == "get_refund_status");
    }
}

public class PolicyRetrievalUpgradeTests
{
    private readonly PolicyRetrieval _retrieval = new();

    [Fact]
    public void Synonym_CancelMapsToFreeCancelPolicy()
    {
        var matches = _retrieval.Retrieve(
            new HotelOrder { Status = "CONFIRMED", PolicyId = "POL-A" },
            new PolicySnapshot { PolicyId = "POL-A", Title = "免费取消", Summary = "截止前免费" },
            "free_cancel");
        Assert.True(matches.Count >= 3);
        Assert.True(matches[0].Score >= matches[1].Score);
        Assert.Contains(matches, m => m.Summary.Contains("关键词") || m.Summary.Contains("意图") || m.PolicyId.Contains("001") || m.PolicyId == "POL-A");
    }

    [Fact]
    public void SnapshotPolicy_GetsBoostedIntoTop()
    {
        var matches = _retrieval.Retrieve(
            new HotelOrder { Status = "CONFIRMED", PolicyId = "HTL-REFUND-005" },
            new PolicySnapshot { PolicyId = "HTL-REFUND-005", Title = "航班取消等不可抗力材料规则", Summary = "材料齐备" },
            "flight_cancelled");
        Assert.Equal("HTL-REFUND-005", matches[0].PolicyId);
        Assert.True(matches[0].Score >= 0.5);
    }
}
