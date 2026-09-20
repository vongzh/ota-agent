using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Plugins.Refund.Ai;

/// <summary>Refund-vertical deterministic tool picker (formerly core ToolIntentPlanner).</summary>
public sealed class RefundDeterministicIntentPlanner : IDeterministicIntentPlanner
{
    public string PluginId => "refund";

    public IReadOnlyList<string> Select(
        string message,
        string conversationState,
        IReadOnlyCollection<string> availableTools,
        string? preferredWriteTool = null)
    {
        var available = new HashSet<string>(availableTools, StringComparer.Ordinal);
        var picks = new List<string>();

        void Add(string name)
        {
            if (available.Contains(name) && !picks.Contains(name, StringComparer.Ordinal))
                picks.Add(name);
        }

        var msg = message ?? "";
        var state = conversationState ?? "";

        if (LooksLikeOrderLookup(msg) || state is "START" or "INTENT_READY" or "ORDER_SELECTION_REQUIRED" or "")
            Add("list_user_orders");
        Add("get_order_detail");

        if (LooksLikeCancel(msg) || LooksLikeFee(msg))
        {
            Add("get_policy_snapshot");
            Add("calculate_refund_quote");
            Add("validate_action_permission");
        }

        if (LooksLikeProgress(msg))
        {
            Add("get_refund_status");
            Add("get_payment_events");
            Add("schedule_deadline_action");
        }

        if (LooksLikeNoRoom(msg))
        {
            Add("verify_fulfillment_issue");
            Add("get_alternative_hotels");
            Add("get_guarantee_quote");
            Add("create_human_handoff");
        }

        if (LooksLikeNegotiate(msg))
        {
            Add("build_supplier_case_draft");
            Add("list_after_sale_events");
        }

        if (LooksLikeEvidence(msg))
        {
            Add("submit_evidence_metadata");
            Add("extract_evidence_fields");
            Add("create_exception_review");
        }

        if (LooksLikeDispute(msg))
        {
            Add("create_service_dispute_case");
            Add("create_human_handoff");
        }

        if (LooksLikeChange(msg))
            Add("get_change_quote");

        if (LooksLikeFinance(msg))
        {
            Add("get_payment_events");
            Add("create_finance_case");
        }

        if (LooksLikeCrossBorder(msg))
        {
            Add("get_responsibility_chain");
            Add("create_human_handoff");
        }

        if (LooksLikeGroup(msg))
        {
            Add("get_group_order_breakdown");
            Add("get_partial_cancel_quote");
            Add("create_human_handoff");
        }

        if (!string.IsNullOrWhiteSpace(preferredWriteTool))
            Add(preferredWriteTool!);

        if (picks.Count == 0)
        {
            Add("get_order_detail");
            Add("get_policy_snapshot");
        }

        return picks.Take(6).ToList();
    }

    private static bool LooksLikeOrderLookup(string m) =>
        ContainsAny(m, "订单", "查一下", "我的酒店", "帮我看看");

    private static bool LooksLikeCancel(string m) =>
        ContainsAny(m, "取消", "退订", "不要了", "不去了");

    private static bool LooksLikeFee(string m) =>
        ContainsAny(m, "扣多少", "费用", "手续费", "免费取消");

    private static bool LooksLikeProgress(string m) =>
        ContainsAny(m, "到账", "退款进度", "还没收到", "钱没到", "多久到");

    private static bool LooksLikeNoRoom(string m) =>
        ContainsAny(m, "没房", "无房", "前台", "到店", "加价换房");

    private static bool LooksLikeNegotiate(string m) =>
        ContainsAny(m, "争取", "协商", "不能退", "例外");

    private static bool LooksLikeEvidence(string m) =>
        ContainsAny(m, "航班", "证明", "疾病", "灾害", "凭证");

    private static bool LooksLikeDispute(string m) =>
        ContainsAny(m, "不一样", "很脏", "卫生", "描述不符");

    private static bool LooksLikeChange(string m) =>
        ContainsAny(m, "改日期", "改期", "订错", "改房型", "改名");

    private static bool LooksLikeFinance(string m) =>
        ContainsAny(m, "重复扣", "扣了两次", "押金", "预授权");

    private static bool LooksLikeCrossBorder(string m) =>
        ContainsAny(m, "海外", "跨境", "代理", "谁负责");

    private static bool LooksLikeGroup(string m) =>
        ContainsAny(m, "团体", "八间", "部分取消", "发票", "公司订");

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));
}
