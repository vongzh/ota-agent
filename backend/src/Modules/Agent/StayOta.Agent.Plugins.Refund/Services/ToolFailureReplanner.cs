using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Plugins.Refund.Services;

/// <summary>
/// Suggests fallback tools when a ToolGateway call is denied or fails.
/// Keeps the agent loop recoverable instead of aborting the pipeline.
/// </summary>
public static class ToolFailureReplanner
{
    public static IReadOnlyList<ReplanSuggestion> Suggest(
        string failedTool,
        string? denyReason,
        string decisionAction,
        RiskLevel riskLevel)
    {
        var reason = denyReason ?? "";
        var suggestions = new List<ReplanSuggestion>();

        if (reason.Contains("L3", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("escalate", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new("create_human_handoff", "OPTION_PRESENTED",
                "L3/资金写被拒，升级人工协同"));
            return suggestions;
        }

        if (reason.Contains("confirmation/", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("confirmation token", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("confirmation_missing", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("invalid or expired confirmation", StringComparison.OrdinalIgnoreCase) ||
            (reason.Contains("confirmation", StringComparison.OrdinalIgnoreCase) &&
             reason.Contains("required", StringComparison.OrdinalIgnoreCase) &&
             !reason.Contains("not allowed in state", StringComparison.OrdinalIgnoreCase)))
        {
            // Do not auto-retry writes without a fresh token — surface HITL.
            suggestions.Add(new("validate_action_permission", "DECISION_READY",
                "确认令牌无效，回到权限校验等待用户确认"));
            return suggestions;
        }

        if (reason.Contains("not allowed in state", StringComparison.OrdinalIgnoreCase))
        {
            var altState = AlternateStateFor(failedTool);
            if (altState is not null)
                suggestions.Add(new(failedTool, altState, $"会话状态不匹配，改用状态 {altState} 重试"));
            suggestions.Add(new("get_order_detail", "ORDER_CONFIRMED", "回读订单事实后再继续"));
            return Dedup(suggestions);
        }

        if (reason.Contains("unknown tool", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("identity", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new("list_user_orders", "INTENT_READY", "身份/工具异常，回到用户订单列表"));
            return suggestions;
        }

        // Generic recovery by decision action
        switch (decisionAction)
        {
            case "ExplainProgress":
                suggestions.Add(new("get_refund_status", "TRACKING_REFUND", "进度查询失败，改查退款状态"));
                suggestions.Add(new("get_payment_events", "TRACKING_REFUND", "补充支付通道事件"));
                break;
            case "Recovery" or "HumanHandoff":
                suggestions.Add(new("create_human_handoff", "OPTION_PRESENTED", "履约工具失败，转人工"));
                suggestions.Add(new("get_alternative_hotels", "DECISION_READY", "尝试替代酒店列表"));
                break;
            case "NegotiateWithHotel":
                suggestions.Add(new("build_supplier_case_draft", "FACTS_REQUIRED", "协商写失败，先重建草案"));
                break;
            case "ConfirmCancel" or "ChangeOrder":
                suggestions.Add(new("calculate_refund_quote", "DECISION_READY", "写操作失败，回到报价"));
                suggestions.Add(new("validate_action_permission", "DECISION_READY", "重新校验权限"));
                break;
            default:
                suggestions.Add(new("get_order_detail", "ORDER_CONFIRMED", "通用回退：重读订单"));
                if (riskLevel == RiskLevel.L3)
                    suggestions.Add(new("create_human_handoff", "OPTION_PRESENTED", "高风险通用升级"));
                break;
        }

        // Never suggest retrying the exact same failed write without a new plan reason.
        return Dedup(suggestions.Where(s => s.ToolName != failedTool || s.Reason.Contains("状态")).ToList());
    }

    private static string? AlternateStateFor(string toolName) => toolName switch
    {
        "get_order_detail" => "FACTS_REQUIRED",
        "get_policy_snapshot" => "DECISION_READY",
        "calculate_refund_quote" => "DECISION_READY",
        "submit_cancellation" => "CONFIRMATION_REQUIRED",
        "create_human_handoff" => "DECISION_READY",
        "get_refund_status" => "WAITING_EXTERNAL",
        _ => "DECISION_READY"
    };

    private static List<ReplanSuggestion> Dedup(IEnumerable<ReplanSuggestion> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ReplanSuggestion>();
        foreach (var item in items)
        {
            var key = $"{item.ToolName}|{item.ConversationState}";
            if (seen.Add(key)) list.Add(item);
        }
        return list.Take(3).ToList();
    }
}

public sealed record ReplanSuggestion(string ToolName, string ConversationState, string Reason);
