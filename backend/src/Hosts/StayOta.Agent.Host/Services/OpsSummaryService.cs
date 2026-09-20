using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Host.Services;

/// <summary>
/// Aggregates Agent-local process metrics for the ops dashboard.
/// Does not invent StayOTA business north-stars (inbound volume, refund settlement, etc.).
/// </summary>
public sealed class OpsSummaryService(
    IAgentSessionStore sessions,
    IRefundDataStore dataStore,
    IEvalRunner evalRunner,
    IAgentPluginRegistry plugins)
{
    private static readonly HashSet<string> WorkflowSuccess = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUCCEEDED", "COMPLETED"
    };

    private static readonly HashSet<string> WorkflowFailed = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASSERTION_FAILED", "REJECTED", "FAILED"
    };

    public async Task<OpsSummaryDto> BuildAsync(CancellationToken ct = default)
    {
        var sessionList = await sessions.ListAsync(200, ct);
        var audits = await dataStore.ListRecentToolAuditsAsync(1000, ct);
        var workflows = await dataStore.ListRecentWorkflowRunsAsync(500, ct);
        var cases = await dataStore.ListRecentCasesAsync(500, ct);
        var evalCases = evalRunner.ListCases();

        var sessionDto = new OpsSessionsDto(
            sessionList.Count,
            sessionList.Count(s => s.PendingApprovalCount > 0),
            sessionList.Sum(s => s.PendingApprovalCount));

        var allowed = audits.Count(a => a.Allowed);
        var denied = audits.Count(a => !a.Allowed);
        var denyRate = audits.Count == 0 ? 0d : (double)denied / audits.Count;
        var auditDto = new OpsAuditsDto(audits.Count, allowed, denied, Math.Round(denyRate, 4));

        var wfSucceeded = workflows.Count(w => WorkflowSuccess.Contains(w.Status));
        var wfFailed = workflows.Count(w => WorkflowFailed.Contains(w.Status));
        var wfRunning = workflows.Count - wfSucceeded - wfFailed;
        var workflowDto = new OpsWorkflowsDto(workflows.Count, wfSucceeded, wfFailed, Math.Max(0, wfRunning));

        var byStatus = cases
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Status) ? "UNKNOWN" : c.Status)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var caseDto = new OpsCasesDto(cases.Count, byStatus);

        var evalDto = new OpsEvalDto(evalCases.Count);
        var pluginItems = plugins.Plugins.Select(p =>
            new OpsPluginItemDto(p.Id, p.DisplayName, ReferenceEquals(p, plugins.Primary))).ToList();
        var pluginDto = new OpsPluginsDto(pluginItems.Count, pluginItems);

        var allowRatePct = audits.Count == 0 ? 100d : Math.Round(100d * allowed / audits.Count, 1);
        var northStar = new OpsNorthStarDto(
            "Tool 门禁放行率（Agent 过程指标）",
            audits.Count == 0 ? "—" : $"{allowRatePct:0.#}%",
            audits.Count == 0
                ? "尚无 Tool 审计；跑一轮对话或 Workflow 后可见"
                : $"基于近 {audits.Count} 条 Tool 审计；非业务退款闭环率",
            IsProcessMetric: true);

        var business = new OpsBusinessPlaceholderDto(
            "正确退款任务闭环率",
            "pending",
            "业务侧待接入（进线量、真实退款到账等不在本仓聚合）");

        var metrics = new List<OpsMetricDto>
        {
            new("会话", "活跃 Session", sessionDto.Total.ToString(),
                sessionDto.WithPendingApproval > 0
                    ? $"{sessionDto.WithPendingApproval} 个含待审批"
                    : "来自 IAgentSessionStore",
                sessionDto.WithPendingApproval > 0 ? "warn" : "ok"),
            new("审批", "待审批请求", sessionDto.PendingApprovalTotal.ToString(),
                "HITL / FunctionApproval 挂起数",
                sessionDto.PendingApprovalTotal > 0 ? "warn" : "ok"),
            new("风险", "Tool 拦截率",
                audits.Count == 0 ? "—" : $"{Math.Round(denyRate * 100, 1):0.#}%",
                denied == 0 ? "暂无拦截记录" : $"{denied} 次拒绝 / {audits.Count} 次调用",
                denied > 0 ? "warn" : "ok"),
            new("能力", "Eval 用例", evalDto.CaseCount.ToString(),
                "离线评测目录规模（本仓可跑）", "ok"),
            new("编排", "Workflow 成功率",
                workflows.Count == 0 ? "—" : $"{Math.Round(100d * wfSucceeded / workflows.Count, 1):0.#}%",
                workflows.Count == 0 ? "尚无 Workflow 落库" : $"{wfSucceeded}/{workflows.Count} 成功",
                workflows.Count > 0 && wfFailed > 0 ? "warn" : "ok"),
            new("插件", "已加载插件", pluginDto.Count.ToString(),
                string.Join("、", pluginItems.Select(p => p.Id)), "ok"),
        };

        var funnel = BuildFunnel(sessionDto.Total, audits.Count, allowed, workflows.Count, cases.Count);
        var riskItems = BuildRiskItems(audits);

        return new OpsSummaryDto(
            DateTimeOffset.UtcNow,
            "agent-process",
            northStar,
            business,
            metrics,
            funnel,
            riskItems,
            sessionDto,
            auditDto,
            workflowDto,
            caseDto,
            evalDto,
            pluginDto);
    }

    private static IReadOnlyList<OpsFunnelStageDto> BuildFunnel(
        int sessions, int audits, int allowed, int workflows, int cases)
    {
        // Agent process funnel — widths relative to the max stage for UI.
        var stages = new (string Stage, int In, string Note)[]
        {
            ("Agent Session", sessions, "会话快照"),
            ("Tool 审计", audits, "含放行与拦截"),
            ("门禁放行", allowed, "Allowed=true"),
            ("Workflow 落库", workflows, "编排运行记录"),
            ("Case 更新", cases, "RefundCase 状态"),
        };

        var result = new List<OpsFunnelStageDto>(stages.Length);
        for (var i = 0; i < stages.Length; i++)
        {
            var prev = i == 0 ? stages[i].In : stages[i - 1].In;
            var drop = Math.Max(0, prev - stages[i].In);
            result.Add(new OpsFunnelStageDto(stages[i].Stage, stages[i].In, drop, stages[i].Note));
        }
        return result;
    }

    private static IReadOnlyList<OpsRiskItemDto> BuildRiskItems(
        IReadOnlyList<StayOta.Agent.Abstractions.Domain.Entities.ToolAuditLog> audits)
    {
        var denied = audits.Where(a => !a.Allowed).ToList();
        if (denied.Count == 0)
        {
            return
            [
                new OpsRiskItemDto("暂无拦截", 0, "低", "关闭", "Tool Policy")
            ];
        }

        return denied
            .GroupBy(a => string.IsNullOrWhiteSpace(a.DenyReason) ? a.ToolName : a.DenyReason!)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g =>
            {
                var sample = g.First();
                var risk = sample.Access == StayOta.Agent.Abstractions.Domain.ToolAccess.Write ? "高" : "中";
                return new OpsRiskItemDto(
                    Truncate(g.Key, 48),
                    g.Count(),
                    risk,
                    "发现",
                    sample.ToolName);
            })
            .ToList();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
