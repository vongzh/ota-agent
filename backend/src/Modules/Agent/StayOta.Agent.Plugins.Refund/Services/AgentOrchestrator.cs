using System.Text.Json;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Refund.Services;

/// <summary>
/// Thin orchestrator: route → intent → policy → rules → assemble context.
/// Tool selection and execution belong to Agent → ToolGateway (sole execution surface).
/// </summary>
public sealed class AgentOrchestrator(
    IRefundDataStore store,
    IIntentService intentService,
    IPolicyRetrieval retrieval,
    IRulesEngine rules,
    IAgentHost agentHost,
    IAgentConversationService conversation,
    IAgentSessionStore agentSessionStore,
    IConfirmationStore confirmationStore,
    ISessionStore sessionStore,
    IVerifier verifier,
    IProductionOrderClient production,
    IToolPolicy toolPolicy,
    Microsoft.Extensions.Options.IOptions<HostingOptions> hostingOptions,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    private readonly ScenarioRouter _router = new();

    public async Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default)
    {
        if (request.ServiceError)
            throw new InvalidOperationException("模拟订单服务响应超时");

        var hosting = hostingOptions.Value;
        if (request.ResetDemo)
        {
            if (!hosting.DemoEnabled)
                throw new InvalidOperationException("ResetDemo is disabled outside demo mode");
            await store.ResetDemoAsync(ct);
        }
        else await store.EnsureSeededAsync(ct);

        var scenarioId = _router.Route(request.Message, request.ScenarioId);
        var scenario = store.GetScenario(scenarioId);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"missing order {scenario.OrderId}");
        var policy = await store.GetPolicyAsync(order.PolicyId, ct)
                     ?? new PolicySnapshot { PolicyId = order.PolicyId, Title = "默认政策", Summary = "演示", RuleCode = "DEFAULT" };

        var signals = new AgentSignals(request.HasEvidence, request.HasNegotiationReason, request.LowConfidence, request.ServiceError, request.ConfirmWrite);
        var traceId = $"trc_{Guid.NewGuid():N}"[..16];
        var runId = $"run_{Guid.NewGuid():N}"[..16];
        var userId = request.UserId ?? scenario.UserId;
        var steps = new List<DecisionStepDto>();

        var analyzed = intentService.Analyze(request.Message, scenario, request.LowConfidence);
        steps.Add(new("意图识别", analyzed.Confidence < 0.5 ? "warning" : "success", analyzed.Intent, analyzed.Confidence));

        if (request.HasEvidence) analyzed.Slots["evidence"] = "uploaded";
        if (request.HasNegotiationReason) analyzed.Slots["negotiation_reason"] = "provided";
        var missing = (signals.HasEvidence || !NeedsEvidence(scenario.ScenarioId, signals)) ? 0 : 1;
        steps.Add(new("槽位提取", missing > 0 ? "warning" : "success", missing > 0 ? $"缺失 {missing} 项" : "槽位完整"));

        steps.Add(new("订单查询", "success", $"status={order.Status}, on_site={order.UserOnSite}, source={production.Mode}"));

        var matches = retrieval.Retrieve(order, policy, analyzed.Reason);
        steps.Add(new("政策检索", "success", $"{matches[0].PolicyId} · score={matches[0].Score:0.00}"));

        var decision = rules.Evaluate(order, policy, scenario, signals);
        steps.Add(new("规则校验", decision.NeedsEvidence ? "warning" : "success", decision.RuleCode));
        steps.Add(new("风险判断", "success", $"{decision.RiskLevel} · {decision.RiskScore}"));

        var requiredTools = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        var writeToolName = scenario.ScenarioId == "I" ? "submit_order_change" : "submit_cancellation";
        var deferWriteToFunctionApproval = decision.NeedsUserConfirm && !request.ConfirmWrite;

        string? confirmationToken = null;
        if (decision.NeedsUserConfirm)
        {
            confirmationToken = await confirmationStore.IssueAsync(
                scenario.CaseId, order.OrderId, order.Version,
                writeToolName,
                TimeSpan.FromMinutes(10), ct);
        }

        // Soft hints — Agent executes via Gateway (sole surface).
        // ConfirmWrite: include confirm tools so Agent runs them with ambient token (no Orchestrator bypass).
        // Else: strip confirm tools; PreferredWriteTool triggers FunctionApproval.
        var hintTools = requiredTools
            .Where(t => request.ConfirmWrite || !toolPolicy.RequiresConfirmation(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        steps.Add(new("处理动作", "active", decision.Action));

        TicketDto? ticket = null;
        if (decision.Action is "HumanHandoff" or "NegotiateWithHotel" or "Recovery" or "FinanceReview" or "SpecialReview" or "ServiceDispute")
        {
            ticket = new TicketDto(
                $"TKT-{scenario.ScenarioId}-{DateTime.UtcNow:HHmmss}",
                decision.RiskLevel == RiskLevel.L3 ? "P1" : "P2",
                decision.RiskLevel == RiskLevel.L3 ? "urgent" : "specialist",
                decision.PlanTitle,
                [
                    $"订单 {order.OrderId} / {order.HotelName}",
                    $"政策 {policy.PolicyId}",
                    $"结论 {decision.Conclusion}",
                    $"风险 {decision.RiskLevel}/{decision.RiskScore}"
                ],
                BuildTicketLifecycle(decision.Action, decision.RiskLevel, decision.CaseStatus));
        }

        HitlStateDto? hitl = null;
        if (decision.NeedsUserConfirm)
        {
            hitl = new HitlStateDto(
                true,
                writeToolName,
                confirmationToken,
                deferWriteToFunctionApproval
                    ? "FunctionApproval (ToolApprovalRequestContent) + confirmation_token + version + idempotency"
                    : "confirmation_token + expected_order_version + idempotency_key");
        }

        var suggestedReply = BuildReply(decision, order);
        var ambient = new Dictionary<string, object?>
        {
            ["refund"] = decision.RefundAmount,
            ["fee"] = decision.FeeAmount,
            ["summary"] = decision.Conclusion,
            ["reason"] = analyzed.Reason,
            ["action"] = decision.Action
        };

        var agentTurn = await conversation.RunTurnAsync(new AgentTurnRequest(
            request.Message,
            traceId,
            userId,
            order.OrderId,
            scenario.CaseId,
            scenario.ScenarioId,
            decision.RiskLevel,
            decision.ConversationState,
            hintTools,
            suggestedReply,
            deferWriteToFunctionApproval,
            deferWriteToFunctionApproval ? writeToolName : null,
            ambient,
            confirmationToken,
            request.IdempotencyKey ?? (decision.NeedsUserConfirm ? $"idem-{scenario.ScenarioId}-{order.OrderId}-{writeToolName}" : null),
            decision.NeedsUserConfirm ? order.Version : null,
            request.AgentSessionId,
            AllowAutonomousToolSelection: hintTools.Count == 0), ct);

        steps.Add(new(
            "Agent 驱动",
            agentTurn.AgentDriven ? "success" : "warning",
            agentTurn.HasPendingApprovals
                ? $"FunctionApproval 待批 ×{agentTurn.PendingApprovals.Count}; session={agentTurn.SessionId}"
                : $"provider={agentHost.ProviderName}, tools={string.Join(',', agentTurn.ToolsInvoked)}, session={agentTurn.SessionId}"));

        var executed = agentTurn.ToolsInvoked.ToList();

        var pendingApprovals = agentTurn.PendingApprovals
            .Select(p => new PendingApprovalDto(p.RequestId, p.CallId, p.ToolName, p.Arguments, p.Description))
            .ToList();

        var refundCase = new RefundCase
        {
            CaseId = scenario.CaseId,
            OrderId = order.OrderId,
            UserId = userId,
            ScenarioId = scenario.ScenarioId,
            Status = decision.CaseStatus,
            RiskLevel = decision.RiskLevel,
            Intent = analyzed.Intent,
            RecommendedAction = decision.Action,
            QuoteRefundAmount = decision.RefundAmount,
            QuoteFeeAmount = decision.FeeAmount,
            ConversationState = decision.ConversationState,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.UpsertCaseAsync(refundCase, ct);
        await store.AppendEventAsync(scenario.CaseId, "agent_decision", new
        {
            decision.Action,
            decision.RuleCode,
            executed,
            confirmationToken,
            agentSessionId = agentTurn.SessionId,
            pendingApprovals = pendingApprovals.Select(p => p.ToolName),
            hintTools
        }, ct);

        var run = new WorkflowRun
        {
            RunId = runId,
            CaseId = scenario.CaseId,
            ScenarioId = scenario.ScenarioId,
            Status = agentTurn.HasPendingApprovals ? "WAITING_APPROVAL" : "COMPLETED",
            TraceJson = JsonSerializer.Serialize(steps),
            ToolSequenceJson = JsonSerializer.Serialize(executed.Distinct()),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await store.SaveWorkflowRunAsync(run, ct);
        await sessionStore.SetAsync(
            $"session:{userId}:{order.OrderId}",
            JsonSerializer.Serialize(new { scenario.CaseId, runId, confirmationToken, agentSessionId = agentTurn.SessionId }),
            TimeSpan.FromHours(6), ct);

        logger.LogInformation(
            "Scenario {Scenario} action {Action} aiProvider={Provider} agentDriven={Driven} pendingApprovals={Pending} production={Mode} hints={Hints}",
            scenario.ScenarioId, decision.Action, agentHost.ProviderName, agentTurn.AgentDriven,
            pendingApprovals.Count, production.Mode, hintTools.Count);

        var dto = new AgentDecisionDto(
            traceId, runId, scenario.CaseId, scenario.ScenarioId,
            analyzed.Intent, analyzed.Confidence, decision.RiskLevel, decision.RiskScore,
            decision.Action, decision.Conclusion, decision.PlanTitle, decision.PlanCopy,
            decision.RefundAmount, decision.FeeAmount,
            agentTurn.Reply,
            decision.ConversationState, decision.CaseStatus,
            steps, analyzed.Slots, matches, executed.Distinct().ToList(), ticket,
            new HotelOrderDto(order.OrderId, order.HotelName, order.CheckIn, order.CheckOut, order.PaidAmount, order.Currency,
                order.Status, order.UserOnSite, order.PolicyId, order.Version, order.RoomType, order.RoomCount),
            false, Array.Empty<string>(), hitl, agentHost.ProviderName,
            agentTurn.SessionId, agentTurn.AgentDriven, agentTurn.HasPendingApprovals, pendingApprovals, production.Mode);
        var verification = verifier.VerifyDecision(dto);
        return dto with { VerificationPassed = verification.Passed, VerificationViolations = verification.Violations };
    }

    public async Task<AgentDecisionDto> RespondToApprovalAsync(FunctionApprovalRequest request, CancellationToken ct = default)
    {
        var snapshot = await agentSessionStore.GetAsync(request.SessionId, ct)
                       ?? throw new InvalidOperationException("agent session not found or expired");

        var agentTurn = await conversation.RespondToApprovalAsync(
            new ApprovalResponseRequest(request.SessionId, request.RequestId, request.Approved, request.Reason), ct);

        var order = await store.GetOrderAsync(snapshot.OrderId, ct)
                    ?? throw new InvalidOperationException($"missing order {snapshot.OrderId}");
        var risk = Enum.TryParse<RiskLevel>(snapshot.RiskLevel, out var rl) ? rl : RiskLevel.L1;
        var pending = agentTurn.PendingApprovals
            .Select(p => new PendingApprovalDto(p.RequestId, p.CallId, p.ToolName, p.Arguments, p.Description))
            .ToList();

        decimal? refund = AmbientDec(snapshot, "refund");
        decimal? fee = AmbientDec(snapshot, "fee");
        var action = request.Approved ? "WriteApproved" : "WriteRejected";
        var caseStatus = request.Approved
            ? (pending.Count > 0 ? "WAITING_APPROVAL" : "REFUND_INITIATED")
            : "AWAITING_USER";
        var runId = $"apr_{Guid.NewGuid():N}"[..16];
        var scenarioId = string.IsNullOrWhiteSpace(snapshot.ScenarioId) ? "A" : snapshot.ScenarioId;

        await store.AppendEventAsync(snapshot.CaseId, "function_approval", new
        {
            request.RequestId,
            request.Approved,
            request.Reason,
            tools = agentTurn.ToolsInvoked,
            pending = pending.Select(p => p.ToolName)
        }, ct);

        var steps = new List<DecisionStepDto>
        {
            new("FunctionApproval", request.Approved ? "success" : "warning",
                request.Approved ? $"已批准 {request.RequestId}" : $"已拒绝 {request.RequestId}"),
            new("Agent 续跑", agentTurn.AgentDriven ? "success" : "warning",
                agentTurn.HasPendingApprovals
                    ? $"仍有待批 ×{pending.Count}"
                    : string.Join(',', agentTurn.ToolsInvoked))
        };

        await store.UpsertCaseAsync(new RefundCase
        {
            CaseId = snapshot.CaseId,
            OrderId = order.OrderId,
            UserId = snapshot.UserId,
            ScenarioId = scenarioId,
            Status = caseStatus,
            RiskLevel = risk,
            Intent = "function_approval",
            RecommendedAction = action,
            QuoteRefundAmount = refund,
            QuoteFeeAmount = fee,
            ConversationState = snapshot.ConversationState,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        await store.SaveWorkflowRunAsync(new WorkflowRun
        {
            RunId = runId,
            CaseId = snapshot.CaseId,
            ScenarioId = scenarioId,
            Status = agentTurn.HasPendingApprovals ? "WAITING_APPROVAL" : (request.Approved ? "COMPLETED" : "REJECTED"),
            TraceJson = JsonSerializer.Serialize(steps),
            ToolSequenceJson = JsonSerializer.Serialize(agentTurn.ToolsInvoked),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        }, ct);

        var dto = new AgentDecisionDto(
            snapshot.TraceId,
            runId,
            snapshot.CaseId,
            scenarioId,
            "function_approval",
            1.0,
            risk,
            risk == RiskLevel.L3 ? 90 : 40,
            action,
            agentTurn.Reply,
            request.Approved ? "写操作已批准" : "写操作已拒绝",
            agentTurn.Reply,
            refund, fee,
            agentTurn.Reply,
            snapshot.ConversationState,
            caseStatus,
            steps,
            new Dictionary<string, string>(),
            [],
            agentTurn.ToolsInvoked.ToList(),
            null,
            new HotelOrderDto(order.OrderId, order.HotelName, order.CheckIn, order.CheckOut, order.PaidAmount, order.Currency,
                order.Status, order.UserOnSite, order.PolicyId, order.Version, order.RoomType, order.RoomCount),
            false, Array.Empty<string>(),
            pending.Count > 0
                ? new HitlStateDto(true, pending[0].ToolName, snapshot.ConfirmationToken,
                    "FunctionApproval (ToolApprovalRequestContent)")
                : null,
            agentHost.ProviderName,
            agentTurn.SessionId,
            agentTurn.AgentDriven,
            agentTurn.HasPendingApprovals,
            pending,
            production.Mode);

        var verification = verifier.VerifyDecision(dto);
        return dto with { VerificationPassed = verification.Passed, VerificationViolations = verification.Violations };
    }

    private static decimal? AmbientDec(AgentSessionSnapshot snapshot, string key)
    {
        if (!snapshot.AmbientArguments.TryGetValue(key, out var v) || v is null) return null;
        try { return Convert.ToDecimal(v); }
        catch { return null; }
    }

    private static IReadOnlyList<TicketLifecycleStepDto> BuildTicketLifecycle(string action, RiskLevel risk, string caseStatus)
    {
        var queue = risk == RiskLevel.L3 ? "紧急专席" : "专项队列";
        return
        [
            new("受理建单", "done", $"已创建 {queue} 工单上下文"),
            new("事实汇总", "done", "订单 / 政策 / 风险已写入工单摘要"),
            new("专席认领", caseStatus is "ESCALATED" or "WAITING_EXTERNAL" or "SPECIAL_REVIEW" ? "active" : "pending",
                action is "NegotiateWithHotel" ? "等待供应商回执" : "等待专员接手"),
            new("用户可见更新", "pending", "公开进度文案待专席确认后同步"),
            new("结案回写", "pending", "恢复会话与审计 Trace 待闭环")
        ];
    }

    public async IAsyncEnumerable<AgentStreamEvent> HandleStreamAsync(
        AgentMessageRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgentStreamEvent("status", "started");
        yield return new AgentStreamEvent("status", "assembling_context");

        AgentDecisionDto? decision = null;
        string? error = null;
        try
        {
            decision = await HandleAsync(request, ct);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is not null)
        {
            yield return new AgentStreamEvent("error", error);
            yield break;
        }

        yield return new AgentStreamEvent("status", "agent_completed");

        foreach (var step in decision!.Steps)
        {
            ct.ThrowIfCancellationRequested();
            yield return new AgentStreamEvent("step", step.Step, step);
            await Task.Yield();
        }

        foreach (var tool in decision.ToolSequence)
            yield return new AgentStreamEvent("tool", tool);

        if (decision.HasPendingApprovals)
            yield return new AgentStreamEvent("approval_required", decision.AgentSessionId, decision.PendingApprovals);

        foreach (var chunk in ChunkText(decision.Reply, 12))
        {
            ct.ThrowIfCancellationRequested();
            yield return new AgentStreamEvent("reply_delta", chunk);
            await Task.Delay(12, ct);
        }

        yield return new AgentStreamEvent("done", null, decision);
    }

    private static IEnumerable<string> ChunkText(string text, int size)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        for (var i = 0; i < text.Length; i += size)
            yield return text[i..Math.Min(i + size, text.Length)];
    }

    private static bool NeedsEvidence(string scenarioId, AgentSignals signals) =>
        scenarioId is "G" || (scenarioId is "F" && !signals.HasNegotiationReason);

    private static string BuildReply(RuleDecision d, HotelOrder order) => d.Action switch
    {
        "RequestEvidence" => $"已核对订单 {order.OrderId}（{order.HotelName}）。{d.Conclusion}。请先上传相关证明。",
        "RequestInformation" => $"{d.Conclusion}。请补充无法入住的具体原因后，我再发起协商。",
        "ConfirmCancel" => $"{d.Conclusion}。预计退回 {order.Currency} {d.RefundAmount:0.##}，费用 {d.FeeAmount:0.##}。确认后提交。",
        "Clarify" => d.PlanCopy,
        _ => $"{d.Conclusion}。{d.PlanCopy}"
    };
}

public sealed class EvalRunner(IRefundDataStore store, IAgentOrchestrator orchestrator) : IEvalRunner
{
    private readonly ScenarioRouter _router = new();

    public IReadOnlyList<EvalCaseDto> ListCases()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../eval/agent-eval-cases.json"));
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../eval/agent-eval-cases.json"));
        var root = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
            path = Path.Combine(root, "eval/agent-eval-cases.json");

        if (!File.Exists(path))
        {
            return ScenarioCodes.All.SelectMany(s => Enumerable.Range(1, 3).Select(i =>
                new EvalCaseDto($"EVAL-{s}-{i:00}", $"scenario {s} sample {i}", s, "L1"))).ToList();
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(ParseCase).ToList();
    }

    private static EvalCaseDto ParseCase(JsonElement c)
    {
        static List<string>? StrList(JsonElement el, string name) =>
            el.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => x.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : null;

        decimal? Dec(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

        return new EvalCaseDto(
            c.GetProperty("id").GetString()!,
            c.GetProperty("message").GetString()!,
            c.GetProperty("expected_scenario").GetString()!,
            c.GetProperty("risk_level").GetString()!,
            StrList(c, "expected_tools_subsequence"),
            StrList(c, "forbidden_reply_substrings"),
            c.TryGetProperty("expected_action", out var act) ? act.GetString() : null,
            Dec(c, "min_refund_amount"),
            Dec(c, "max_fee_amount"));
    }

    public async Task<IReadOnlyList<EvalResultDto>> RunAllAsync(CancellationToken ct = default)
    {
        await store.EnsureSeededAsync(ct);
        var results = new List<EvalResultDto>();
        foreach (var c in ListCases())
        {
            var routed = _router.Route(c.Message, null);
            var passed = routed == c.ExpectedScenario;
            string? detail = null;
            if (passed)
            {
                try
                {
                    var decision = await orchestrator.HandleAsync(
                        new AgentMessageRequest(c.Message, c.ExpectedScenario, ResetDemo: false), ct);
                    var violations = new List<string>();
                    if (decision.ScenarioId != c.ExpectedScenario)
                        violations.Add($"scenario={decision.ScenarioId}");
                    if (!string.IsNullOrWhiteSpace(c.ExpectedAction) &&
                        !string.Equals(decision.Action, c.ExpectedAction, StringComparison.Ordinal))
                        violations.Add($"action={decision.Action} expected={c.ExpectedAction}");
                    if (c.ExpectedToolsSubsequence is { Count: > 0 } &&
                        !IsSubsequence(c.ExpectedToolsSubsequence, decision.ToolSequence))
                        violations.Add($"tools missing subsequence [{string.Join(',', c.ExpectedToolsSubsequence)}]");
                    if (c.ForbiddenReplySubstrings is { Count: > 0 })
                    {
                        foreach (var bad in c.ForbiddenReplySubstrings)
                        {
                            if (decision.Reply.Contains(bad, StringComparison.Ordinal))
                                violations.Add($"forbidden reply contains '{bad}'");
                        }
                    }
                    if (c.MinRefundAmount is not null &&
                        (decision.RefundAmount is null || decision.RefundAmount < c.MinRefundAmount))
                        violations.Add($"refund={decision.RefundAmount} < min {c.MinRefundAmount}");
                    if (c.MaxFeeAmount is not null &&
                        decision.FeeAmount is not null && decision.FeeAmount > c.MaxFeeAmount)
                        violations.Add($"fee={decision.FeeAmount} > max {c.MaxFeeAmount}");

                    passed = violations.Count == 0;
                    detail = passed
                        ? $"{decision.Action}/{decision.CaseStatus}; tools={decision.ToolSequence.Count}"
                        : string.Join("; ", violations);
                }
                catch (Exception ex)
                {
                    passed = false;
                    detail = ex.Message;
                }
            }
            else detail = $"routed={routed}";
            results.Add(new EvalResultDto(c.Id, c.Message, c.ExpectedScenario, routed, passed, detail));
        }
        return results;
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var i = 0;
        foreach (var item in actual)
        {
            if (i < expected.Count && item == expected[i]) i++;
        }
        return i == expected.Count;
    }
}
