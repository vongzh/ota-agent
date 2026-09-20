using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Plugins.Refund.Mcp;

/// <summary>
/// Demo-only MCP surface hosted by the Agent. Production business tools must be implemented
/// by StayOTA systems per <c>contracts/business-mcp-protocol.json</c>; Agent then connects as client.
/// Endpoint: <c>/mcp</c>
/// </summary>
[McpServerToolType]
public sealed class RefundMcpTools(
    IProductionOrderClient production,
    IToolGateway gateway,
    ILogger<RefundMcpTools> logger)
{
    [McpServerTool(Name = "stayota_get_order_detail"), Description("读取酒店订单详情（生产直连或 Mock）")]
    public async Task<string> GetOrderDetail(
        [Description("订单号")] string orderId,
        [Description("用户 ID")] string userId,
        CancellationToken ct = default)
    {
        logger.LogInformation("MCP get_order_detail {OrderId} via {Mode}", orderId, production.Mode);
        var data = await production.GetOrderDetailAsync(orderId, userId, ct);
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    [McpServerTool(Name = "stayota_list_user_orders"), Description("列出用户订单")]
    public async Task<string> ListUserOrders(
        [Description("用户 ID")] string userId,
        CancellationToken ct = default)
    {
        var data = await production.ListUserOrdersAsync(userId, ct);
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    [McpServerTool(Name = "stayota_get_policy_snapshot"), Description("读取成交政策快照")]
    public async Task<string> GetPolicySnapshot(
        [Description("政策 ID")] string policyId,
        [Description("订单号")] string orderId,
        CancellationToken ct = default)
    {
        var data = await production.GetPolicySnapshotAsync(policyId, orderId, ct);
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    [McpServerTool(Name = "stayota_get_refund_status"), Description("查询退款进度")]
    public async Task<string> GetRefundStatus(
        [Description("退款单号")] string refundId,
        CancellationToken ct = default)
    {
        var data = await production.GetRefundStatusAsync(refundId, ct);
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    [McpServerTool(Name = "stayota_validate_action_permission"), Description("校验写操作权限与风险门禁")]
    public async Task<string> ValidateActionPermission(
        [Description("用户 ID")] string userId,
        [Description("订单号")] string orderId,
        [Description("案件号")] string caseId,
        [Description("动作")] string action,
        CancellationToken ct = default)
    {
        var result = await gateway.InvokeAsync(new ToolCall(
            $"mcp_{Guid.NewGuid():N}"[..12],
            "validate_action_permission",
            ToolAccess.Read,
            userId,
            orderId,
            caseId,
            RiskLevel.L1,
            "DECISION_READY",
            new Dictionary<string, object?> { ["action"] = action }), ct);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            allowed = result.Allowed,
            success = result.Success,
            deny = result.DenyReason,
            data = result.Data
        });
    }
}
