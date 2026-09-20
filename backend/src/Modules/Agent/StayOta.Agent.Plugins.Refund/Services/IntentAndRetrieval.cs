using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain.Entities;

namespace StayOta.Agent.Plugins.Refund.Services;

public sealed class IntentService : IIntentService
{
    public (string Intent, string Reason, double Confidence, Dictionary<string, string> Slots) Analyze(
        string message, ScenarioFixture scenario, bool lowConfidence)
    {
        if (lowConfidence)
        {
            return ("意图不明确", "unclear", 0.42, new Dictionary<string, string>
            {
                ["order_id"] = scenario.OrderId,
                ["missing"] = "具体诉求"
            });
        }

        var (intent, reason, confidence) = scenario.ScenarioId switch
        {
            "A" => ("申请退款", "free_cancel", 0.94),
            "B" => ("查询扣费并取消", "deducted_cancel", 0.93),
            "C" => ("查询退款进度", "refund_progress", 0.95),
            "D" => ("履约异常求助", "prearrival_no_room", 0.96),
            "E" => ("到店无房紧急求助", "onsite_no_room", 0.97),
            "F" => ("申请例外协商", "non_refundable", 0.92),
            "G" => ("特殊原因退款", "flight_cancelled", 0.93),
            "H" => ("服务争议", "service_dispute", 0.91),
            "I" => ("订单变更", "order_change", 0.92),
            "J" => ("支付异常核查", "payment_anomaly", 0.9),
            "K" => ("责任主体查询", "cross_border", 0.9),
            "L" => ("团体部分取消", "group_partial", 0.91),
            _ => ("申请退款", "general", 0.8)
        };

        return (intent, reason, confidence, new Dictionary<string, string>
        {
            ["order_id"] = scenario.OrderId,
            ["scenario"] = scenario.ScenarioId,
            ["refund_reason"] = reason,
            ["message_preview"] = message.Length > 40 ? message[..40] : message
        });
    }
}

/// <summary>
/// Tag/keyword scored policy retrieval aligned with hotel's retrieval engine.
/// </summary>
public sealed class PolicyRetrieval : IPolicyRetrieval
{
    private const double Threshold = 0.28;

    private static readonly PolicyCorpusItem[] Corpus =
    [
        new("HTL-REFUND-001", "免费取消期内退款规则", 96,
            ["hotel", "free_until", "not_arrived", "personal_change", "CONFIRMED"],
            ["申请退款", "free_cancel"],
            ["免费取消", "免费退款", "不住了", "取消酒店", "杭州"],
            "未入住订单在政策快照约定的免费取消截止时间前，可按原支付路径提交退款。"),
        new("HTL-REFUND-002", "免费取消截止时间计算规则", 82,
            ["hotel", "free_until", "deadline", "policy_snapshot", "CONFIRMED"],
            ["申请退款", "free_cancel"],
            ["截止时间", "几点前", "还来得及", "免费取消期"],
            "取消时效应读取订单生成时保存的政策快照，不使用当前商品页规则追溯判断。"),
        new("HTL-REFUND-003", "不可取消订单协商规则", 95,
            ["hotel", "non_refundable", "not_arrived", "personal_change", "CONFIRMED"],
            ["申请例外协商", "non_refundable"],
            ["不可取消", "特价", "临时有事", "去不了", "协商退款", "不能退"],
            "不可取消产品不支持自动退款；平台可代用户提交酒店协商，最终结果以酒店审核为准。"),
        new("HTL-REFUND-004", "已入住或已核销订单审核规则", 90,
            ["hotel", "arrived", "consumed", "CONFIRMED"],
            ["服务争议", "special_review"],
            ["已入住", "已入住", "核销", "住过"],
            "已入住或已核销订单不得自动退款，需走例外审核并保留证据。"),
        new("HTL-REFUND-005", "航班取消等不可抗力材料规则", 93,
            ["hotel", "force_majeure", "evidence", "CONFIRMED"],
            ["特殊原因退款", "flight_cancelled"],
            ["航班取消", "疾病", "灾害", "证明", "医院证明"],
            "不可抗力材料齐备后可进入例外审核；金额与结论由规则引擎决定。"),
        new("HTL-REFUND-006", "到店无房履约保障规则", 97,
            ["hotel", "fulfillment_exception", "arrived", "CONFIRMED"],
            ["到店无房紧急求助", "履约异常求助", "onsite_no_room", "prearrival_no_room"],
            ["没房", "到前台", "加价", "无法入住"],
            "确认无房后优先保障当晚住宿，财务责任与退款分开处理，L3 必须人工。"),
        new("HTL-REFUND-007", "退款进度与支付调查规则", 88,
            ["hotel", "refund_progress", "CHANNEL_PROCESSING", "CONFIRMED"],
            ["查询退款进度", "refund_progress", "支付异常核查"],
            ["到账", "退款进度", "还没收到", "扣了两次", "预授权"],
            "退款进度以支付通道回执为准；超时触发支付调查，不得口头承诺到账日。"),
        new("HTL-REFUND-008", "服务描述不符争议规则", 86,
            ["hotel", "service_dispute", "manual_review", "CONFIRMED"],
            ["服务争议", "service_dispute"],
            ["图片不一样", "很脏", "描述不符", "卫生"],
            "服务争议需材料与酒店联系结果；可创建服务争议案，不自动判定金额。"),
        new("HTL-REFUND-009", "订单变更与差价规则", 84,
            ["hotel", "order_change", "CONFIRMED"],
            ["订单变更", "order_change"],
            ["订错", "改日期", "改房型", "改名"],
            "变更以变更报价 Tool 回执为准，写操作需确认令牌与订单版本。"),
        new("HTL-REFUND-010", "跨境责任链与团体部分取消", 85,
            ["hotel", "cross_border", "group", "CONFIRMED"],
            ["责任主体查询", "团体部分取消", "cross_border", "group_partial"],
            ["海外", "跨境", "代理", "团体", "八间", "发票"],
            "跨境先厘清责任链；团体部分取消需专席与拆分报价，禁止自动整单退。"),
        new("POL-GENERIC-AUTHORITY", "成交政策快照优先于聊天记忆", 70,
            ["hotel", "policy_snapshot"],
            ["申请退款", "general"],
            ["政策", "快照", "成交"],
            "金额与权限以订单成交时政策快照为准，不得由模型改写。"),
        new("POL-HITL-BOUNDARY", "高风险必须人工接管", 68,
            ["hotel", "manual_review"],
            ["转人工", "HumanHandoff"],
            ["人工", "升级", "高风险"],
            "L3 场景禁止自动写资金动作，需携带上下文升级。"),
    ];

    public IReadOnlyList<PolicyMatchDto> Retrieve(HotelOrder order, PolicySnapshot policy, string reason)
    {
        var queryTags = BuildQueryTags(order, reason);
        var message = $"{policy.Title} {policy.Summary} {reason}";
        var intent = reason;

        var scored = Corpus
            .Select(item => Score(item, message, intent, queryTags, order, policy))
            .Where(m => m.Score >= Threshold)
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Priority)
            .Take(3)
            .Select(m => new PolicyMatchDto(m.PolicyId, m.Title, m.Score, m.Summary))
            .ToList();

        if (scored.Count == 0)
        {
            return
            [
                new(policy.PolicyId, policy.Title, 0.72, policy.Summary),
                new("POL-GENERIC-AUTHORITY", "成交政策快照优先于聊天记忆", 0.61,
                    "金额与权限以订单成交时政策快照为准，不得由模型改写。"),
                new("POL-HITL-BOUNDARY", "高风险必须人工接管", 0.55,
                    "L3 场景禁止自动写资金动作，需携带上下文升级。")
            ];
        }

        // Prefer binding snapshot as #1 when present in corpus hits
        if (scored.All(s => s.PolicyId != policy.PolicyId) && !string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            scored = scored.Take(2).Prepend(new PolicyMatchDto(policy.PolicyId, policy.Title,
                Math.Max(0.8, scored[0].Score), policy.Summary)).ToList();
        }

        return scored;
    }

    private static List<string> BuildQueryTags(HotelOrder order, string reason)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hotel",
            order.Status,
            reason
        };
        if (order.UserOnSite) tags.Add("arrived");
        if (reason.Contains("flight", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("force", StringComparison.OrdinalIgnoreCase))
            tags.Add("force_majeure");
        if (reason.Contains("evidence", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("flight_cancelled", StringComparison.OrdinalIgnoreCase))
            tags.Add("evidence");
        if (reason.Contains("no_room", StringComparison.OrdinalIgnoreCase))
            tags.Add("fulfillment_exception");
        if (reason.Contains("non_refundable", StringComparison.OrdinalIgnoreCase))
            tags.Add("non_refundable");
        if (reason.Contains("refund_progress", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("payment", StringComparison.OrdinalIgnoreCase))
            tags.Add("refund_progress");
        if (reason.Contains("cross_border", StringComparison.OrdinalIgnoreCase))
            tags.Add("cross_border");
        if (reason.Contains("group", StringComparison.OrdinalIgnoreCase))
            tags.Add("group");
        if (reason.Contains("service", StringComparison.OrdinalIgnoreCase))
            tags.Add("service_dispute");
        return tags.ToList();
    }

    private static Scored Score(
        PolicyCorpusItem item, string message, string intent, IReadOnlyList<string> queryTags,
        HotelOrder order, PolicySnapshot snapshot)
    {
        var normalized = Normalize(message);
        var citationNorm = Normalize(item.Citation + item.Title);
        var matchedKeywords = item.Keywords.Where(k => KeywordMatches(normalized, k)).ToList();
        var matchedTags = item.Tags.Where(t => queryTags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        var intentMatch = item.IntentTags.Any(t =>
            intent.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            t.Contains(intent, StringComparison.OrdinalIgnoreCase) ||
            KeywordMatches(Normalize(intent), t));
        var orderStateMatch = item.Tags.Contains(order.Status, StringComparer.OrdinalIgnoreCase)
                              || item.Tags.Contains("CONFIRMED", StringComparer.OrdinalIgnoreCase);
        var bigram = BigramJaccard(normalized, citationNorm);
        var snapshotBoost = item.PolicyId == snapshot.PolicyId ? 0.14 :
            (Normalize(snapshot.Title).Length > 0 && citationNorm.Contains(Normalize(snapshot.Title)) ? 0.06 : 0);

        var keywordScore = Math.Min(0.34, matchedKeywords.Count * 0.17);
        var tagScore = Math.Min(0.28, matchedTags.Count * 0.08);
        var score = Math.Min(
            0.99,
            item.Priority / 1200.0 +
            keywordScore +
            tagScore +
            (intentMatch ? 0.26 : 0) +
            (orderStateMatch ? 0.08 : 0) +
            bigram * 0.22 +
            snapshotBoost);

        var reasons = new List<string>();
        if (intentMatch) reasons.Add($"匹配意图：{intent}");
        if (matchedKeywords.Count > 0) reasons.Add($"命中关键词：{string.Join('、', matchedKeywords)}");
        if (matchedTags.Count > 0) reasons.Add($"匹配业务标签：{string.Join('、', matchedTags)}");
        if (bigram >= 0.12) reasons.Add($"文本相似 {bigram:0.00}");
        if (snapshotBoost > 0) reasons.Add("成交快照加权");

        var summary = reasons.Count == 0
            ? item.Citation
            : $"{item.Citation}（{string.Join('；', reasons)}）";

        return new Scored(item.PolicyId, item.Title, Math.Round(score, 2), summary, item.Priority);
    }

    private static string Normalize(string text) =>
        (text ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private static double BigramJaccard(string a, string b)
    {
        static HashSet<string> Grams(string s)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s)) return set;
            if (s.Length == 1) { set.Add(s); return set; }
            for (var i = 0; i < s.Length - 1; i++) set.Add(s[i..(i + 2)]);
            return set;
        }

        var ga = Grams(a);
        var gb = Grams(b);
        if (ga.Count == 0 || gb.Count == 0) return 0;
        var inter = ga.Count(g => gb.Contains(g));
        var union = ga.Count + gb.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    private static bool KeywordMatches(string normalizedMessage, string keyword)
    {
        var kn = Normalize(keyword);
        if (string.IsNullOrEmpty(kn)) return false;
        if (normalizedMessage.Contains(kn)) return true;
        foreach (var (key, alts) in Synonyms)
        {
            var group = new List<string> { key };
            group.AddRange(alts);
            var knorms = group.Select(Normalize).ToList();
            if (!knorms.Any(g => g == kn || kn.Contains(g) || g.Contains(kn))) continue;
            if (knorms.Any(g => normalizedMessage.Contains(g))) return true;
        }
        return false;
    }

    private static HashSet<string> ExpandSynonyms(string normalized)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { normalized };
        foreach (var (key, alts) in Synonyms)
        {
            if (!normalized.Contains(key) && alts.All(a => !normalized.Contains(a))) continue;
            set.Add(key);
            foreach (var a in alts) set.Add(a);
        }
        return set;
    }

    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["取消"] = ["退订", "不住了", "不去了"],
        ["到账"] = ["退款进度", "钱没到", "还没收到"],
        ["没房"] = ["无房", "无法入住", "到店"],
        ["航班"] = ["不可抗力", "证明", "疾病"],
        ["改期"] = ["改日期", "订错", "改房型"],
        ["跨境"] = ["海外", "代理", "责任链"],
        ["团体"] = ["八间", "部分取消", "发票"],
    };

    private sealed record PolicyCorpusItem(
        string PolicyId,
        string Title,
        int Priority,
        string[] Tags,
        string[] IntentTags,
        string[] Keywords,
        string Citation);

    private sealed record Scored(string PolicyId, string Title, double Score, string Summary, int Priority);
}

public sealed class ScenarioRouter
{
    public string Route(string message, string? explicitScenario)
    {
        if (!string.IsNullOrWhiteSpace(explicitScenario))
            return explicitScenario!.Trim().ToUpperInvariant();

        var text = message ?? "";
        if (ContainsAny(text, "八间", "团体", "企业多房", "部分取消", "发票")) return "L";
        if (ContainsAny(text, "海外", "跨境", "代理又让", "谁负责")) return "K";
        if (ContainsAny(text, "扣了两次", "预授权", "押金", "重复扣款")) return "J";
        if (ContainsAny(text, "订错", "改日期", "改房型", "改名")) return "I";
        if (ContainsAny(text, "图片不一样", "很脏", "描述不符", "卫生")) return "H";
        if (ContainsAny(text, "航班取消", "疾病", "灾害", "证明免费退", "医院证明")) return "G";
        if (ContainsAny(text, "不能退", "不可取消", "争取")) return "F";
        if (ContainsAny(text, "已经在前台", "已经到前台", "到店无房", "无法入住，今晚")) return "E";
        if (ContainsAny(text, "没房", "加价", "加 300", "加300", "加价三百")) return "D";
        if (ContainsAny(text, "到账", "退款进度", "还没收到", "钱没到账")) return "C";
        if (ContainsAny(text, "扣多少", "首晚", "取消费")) return "B";
        if (ContainsAny(text, "免费取消", "取消明天", "杭州")) return "A";
        return "A";
    }

    private static bool ContainsAny(string text, params string[] keys) =>
        keys.Any(k => text.Contains(k, StringComparison.Ordinal));
}
