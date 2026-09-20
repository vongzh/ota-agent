using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Plugins.Refund.Services;
using Xunit;

namespace StayOta.Agent.Tests;

public class ToolGatewayTests
{
    [Fact]
    public async Task ListContracts_Has33Tools()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        Assert.Equal(33, gateway.ListContracts().Count);
    }

    [Fact]
    public async Task ReadTool_AllowedInDeclaredState()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "get_order_detail", ToolAccess.Read, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "ORDER_CONFIRMED", new Dictionary<string, object?>()));
        Assert.True(result.Allowed);
        Assert.True(result.Success);
        Assert.Null(result.DenyReason);
    }

    [Fact]
    public async Task ReadTool_DeniedInWrongConversationState()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "get_order_detail", ToolAccess.Read, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>()));
        Assert.False(result.Allowed);
        Assert.Contains("not allowed in state", result.DenyReason ?? "");
    }

    [Fact]
    public async Task WriteTool_RequiresConfirmationToken()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "submit_cancellation", ToolAccess.Write, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>(),
            ConfirmationToken: null, IdempotencyKey: "idem-1", ExpectedOrderVersion: 1));
        Assert.False(result.Allowed);
        Assert.Contains("confirmation", result.DenyReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteTool_SucceedsWithValidConfirmation()
    {
        var (gateway, _, confirm) = GatewayFactory.Create();
        var token = await confirm.IssueAsync("CASE-A-001", "ORD-A-001", 1, "submit_cancellation", TimeSpan.FromMinutes(5));
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "submit_cancellation", ToolAccess.Write, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>(),
            ConfirmationToken: token, IdempotencyKey: "idem-ok-1", ExpectedOrderVersion: 1));
        Assert.True(result.Allowed);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task L3_BlocksAutoFinancialWrite()
    {
        var (gateway, _, confirm) = GatewayFactory.Create();
        var token = await confirm.IssueAsync("CASE-E-001", "ORD-E-001", 1, "submit_cancellation", TimeSpan.FromMinutes(5));
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "submit_cancellation", ToolAccess.Write, "USR-E", "ORD-E-001", "CASE-E-001",
            RiskLevel.L3, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>(),
            ConfirmationToken: token, IdempotencyKey: "idem-l3", ExpectedOrderVersion: 1));
        Assert.False(result.Allowed);
        Assert.Contains("L3", result.DenyReason ?? "");
    }

    [Fact]
    public async Task UnknownTool_Denied()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "not_a_real_tool", ToolAccess.Read, "USR-A", null, null,
            RiskLevel.L1, "INTENT_READY", new Dictionary<string, object?>()));
        Assert.False(result.Allowed);
        Assert.Contains("unknown tool", result.DenyReason ?? "");
    }

    [Fact]
    public async Task MissingUser_Denied()
    {
        var (gateway, _, _) = GatewayFactory.Create();
        var result = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "list_user_orders", ToolAccess.Read, "", null, null,
            RiskLevel.L1, "INTENT_READY", new Dictionary<string, object?>()));
        Assert.False(result.Allowed);
        Assert.Contains("identity", result.DenyReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Idempotency_SecondCallReplaysCachedPayload()
    {
        var (gateway, _, confirm) = GatewayFactory.Create();
        var token1 = await confirm.IssueAsync("CASE-A-001", "ORD-A-001", 1, "submit_cancellation", TimeSpan.FromMinutes(5));
        var token2 = await confirm.IssueAsync("CASE-A-001", "ORD-A-001", 1, "submit_cancellation", TimeSpan.FromMinutes(5));
        var call = (string token) => gateway.InvokeAsync(new ToolCall(
            "trc_test", "submit_cancellation", ToolAccess.Write, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>(),
            ConfirmationToken: token, IdempotencyKey: "idem-dup", ExpectedOrderVersion: 1));

        var first = await call(token1);
        var second = await call(token2);
        Assert.True(first.Allowed && first.Success);
        Assert.True(second.Allowed && second.Success);
        var secondJson = System.Text.Json.JsonSerializer.Serialize(second.Data);
        Assert.Contains("duplicate", secondJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replay", secondJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refund_id", secondJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetActionResult_ReturnsCompletedIdempotentWrite()
    {
        var (gateway, _, confirm) = GatewayFactory.Create();
        var token = await confirm.IssueAsync("CASE-A-001", "ORD-A-001", 1, "submit_cancellation", TimeSpan.FromMinutes(5));
        var write = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "submit_cancellation", ToolAccess.Write, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "CONFIRMATION_REQUIRED", new Dictionary<string, object?>(),
            ConfirmationToken: token, IdempotencyKey: "idem-lookup-1", ExpectedOrderVersion: 1));
        Assert.True(write.Success);

        var lookup = await gateway.InvokeAsync(new ToolCall(
            "trc_test", "get_action_result", ToolAccess.Read, "USR-A", "ORD-A-001", "CASE-A-001",
            RiskLevel.L1, "ACTION_IN_PROGRESS",
            new Dictionary<string, object?> { ["idempotency_key"] = "idem-lookup-1" },
            IdempotencyKey: "idem-lookup-1"));
        Assert.True(lookup.Allowed && lookup.Success);
        var json = System.Text.Json.JsonSerializer.Serialize(lookup.Data);
        Assert.Contains("\"found\":true", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUCCEEDED", json, StringComparison.OrdinalIgnoreCase);
    }
}
