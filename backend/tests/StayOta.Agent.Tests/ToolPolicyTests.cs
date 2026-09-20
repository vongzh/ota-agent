using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Tools;
using StayOta.Agent.Plugins.Refund;
using StayOta.Agent.Tools;
using Xunit;

namespace StayOta.Agent.Tests;

public class ToolPolicyTests
{
    private static readonly IToolPolicy Policy =
        new CompositeToolPolicy([new RefundToolPolicyContribution()]);

    [Fact]
    public void WriteAndConfirm_AreConsistent()
    {
        Assert.True(Policy.IsWrite("submit_cancellation"));
        Assert.True(Policy.RequiresConfirmation("submit_cancellation"));
        Assert.True(Policy.IsWrite("create_human_handoff"));
        Assert.False(Policy.RequiresConfirmation("create_human_handoff"));
        Assert.False(Policy.IsWrite("get_order_detail"));
        Assert.Equal(ToolAccess.Write, Policy.AccessOf("submit_order_change"));
        Assert.Equal(ToolAccess.Read, Policy.AccessOf("calculate_refund_quote"));
    }

    [Fact]
    public void StateFor_AppliesScenarioOverrides()
    {
        Assert.Equal("WAITING_EXTERNAL", Policy.StateFor("create_human_handoff", "H"));
        Assert.Equal("DECISION_READY", Policy.StateFor("create_human_handoff", "K"));
        Assert.Equal("OPTION_PRESENTED", Policy.StateFor("create_human_handoff", "E"));
        Assert.Equal("CONFIRMATION_REQUIRED", Policy.StateFor("submit_cancellation"));
    }

    [Fact]
    public void EchoContribution_MergesWithoutClobberingRefund()
    {
        var merged = new CompositeToolPolicy(
        [
            new RefundToolPolicyContribution(),
            new StayOta.Agent.Plugins.Echo.EchoAgentPlugin().ToolPolicy
        ]);
        Assert.True(merged.IsWrite("submit_cancellation"));
        Assert.Equal("INTENT_READY", merged.StateFor("echo_ping"));
    }
}
