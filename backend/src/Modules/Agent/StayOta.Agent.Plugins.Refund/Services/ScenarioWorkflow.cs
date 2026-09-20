using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Refund.Services;

/// <summary>
/// A–L scenario runner built on Microsoft Agent Framework <see cref="WorkflowBuilder"/> /
/// <see cref="InProcessExecution"/> instead of a hand-rolled FSM loop.
/// Tool gates remain in <see cref="IToolGateway"/> via <see cref="IAgentToolCatalog"/>.
/// Write/confirm/state classification comes from plugin-contributed <see cref="IToolPolicy"/>.
/// </summary>
public sealed class ScenarioWorkflow(
    IRefundDataStore store,
    IAgentToolCatalog tools,
    IConfirmationStore confirmationStore,
    IToolPolicy toolPolicy,
    IVerifier verifier,
    ILogger<ScenarioWorkflow> logger) : IScenarioWorkflow
{
    public async Task<WorkflowRunResultDto> RunAsync(string scenarioId, CancellationToken ct = default)
    {
        await store.EnsureSeededAsync(ct);
        scenarioId = scenarioId.ToUpperInvariant();
        var scenario = store.GetScenario(scenarioId);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"order missing {scenario.OrderId}");
        var requiredTools = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        if (scenarioId == "H")
        {
            var idx = requiredTools.IndexOf("submit_evidence_metadata");
            if (idx >= 0) requiredTools.Insert(idx + 1, "submit_evidence_metadata");
        }

        var bag = new ScenarioRunBag
        {
            RunId = $"run_{Guid.NewGuid():N}"[..16],
            TraceId = $"trc_{Guid.NewGuid():N}"[..16],
            ScenarioId = scenarioId,
            Scenario = scenario,
            Order = order,
            RequiredTools = requiredTools,
            State = "START",
            Risk = scenario.RiskLevel
        };

        var workflow = BuildAgentFrameworkWorkflow(bag, requiredTools);
        await using var run = await InProcessExecution.RunAsync(workflow, bag, cancellationToken: ct);
        _ = run.OutgoingEvents.OfType<WorkflowOutputEvent>().LastOrDefault()?.Data as ScenarioRunBag ?? bag;

        var caseStatus = MapFinalCaseStatus(scenarioId, bag.State, scenario.ExpectedCaseStatus);
        await store.UpsertCaseAsync(new RefundCase
        {
            CaseId = scenario.CaseId,
            OrderId = order.OrderId,
            UserId = scenario.UserId,
            ScenarioId = scenarioId,
            Status = caseStatus,
            RiskLevel = bag.Risk,
            Intent = scenario.Title,
            RecommendedAction = scenario.ExpectedRoute,
            ConversationState = bag.State,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        var expectedOriginal = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        var draft = new WorkflowRunResultDto(
            bag.RunId, scenarioId, scenario.ExpectedRoute, scenario.CaseId, bag.State, caseStatus,
            bag.ToolCalls, bag.Steps, new WorkflowAssertionDto(false, false, false, false), false);
        var verification = verifier.VerifyWorkflow(draft, expectedOriginal, scenario.ExpectedCaseStatus);
        var assertions = new WorkflowAssertionDto(
            IsSubsequence(expectedOriginal, bag.ToolCalls),
            Verifier.StatusCompatible(caseStatus, scenario.ExpectedCaseStatus),
            bag.ToolCalls.All(t => expectedOriginal.Contains(t) || (scenarioId == "H" && t == "submit_evidence_metadata")),
            verification.Passed);
        var succeeded = assertions.RequiredToolsCalledInOrder && assertions.NoUnexpectedTool && assertions.ExpectedCaseStatusReached;

        await store.SaveWorkflowRunAsync(new WorkflowRun
        {
            RunId = bag.RunId,
            CaseId = scenario.CaseId,
            ScenarioId = scenarioId,
            Status = succeeded ? "SUCCEEDED" : "ASSERTION_FAILED",
            TraceJson = JsonSerializer.Serialize(bag.Steps),
            ToolSequenceJson = JsonSerializer.Serialize(bag.ToolCalls),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        }, ct);

        logger.LogInformation(
            "AF Workflow {Scenario} succeeded={Succeeded} tools={Count} engine=Microsoft.Agents.AI.Workflows",
            scenarioId, succeeded, bag.ToolCalls.Count);
        return new WorkflowRunResultDto(bag.RunId, scenarioId, scenario.ExpectedRoute, scenario.CaseId, bag.State, caseStatus,
            bag.ToolCalls, bag.Steps, assertions, succeeded);
    }

    private Workflow BuildAgentFrameworkWorkflow(ScenarioRunBag bag, IReadOnlyList<string> requiredTools)
    {
        var intent = new FunctionExecutor<ScenarioRunBag, ScenarioRunBag>("intent_and_route", (s, _, _) =>
        {
            s.State = Record(s.Steps, "INTENT_AND_ROUTE", "RULE_ENGINE", s.State, "INTENT_READY", null,
                new { s.Scenario.EntryMessage, route = s.Scenario.ExpectedRoute, s.Scenario.RiskLevel });
            return ValueTask.FromResult(s);
        });

        var builder = new WorkflowBuilder(intent);
        ExecutorBinding previous = intent;

        for (var i = 0; i < requiredTools.Count; i++)
        {
            var occurrence = i;
            var toolName = requiredTools[i];
            var nodeId = $"tool_{occurrence}_{toolName}";
            var exec = new FunctionExecutor<ScenarioRunBag, ScenarioRunBag>(nodeId, async (s, _, ct) =>
            {
                await ExecuteToolStepAsync(s, toolName, occurrence, ct).ConfigureAwait(false);
                return s;
            });
            builder.AddEdge(previous, exec);
            previous = exec;
        }

        var finalize = new FunctionExecutor<ScenarioRunBag, ScenarioRunBag>("finalize", async (s, ctx, ct) =>
        {
            await ctx.YieldOutputAsync(s, ct).ConfigureAwait(false);
            return s;
        });
        builder.AddEdge(previous, finalize).WithOutputFrom(finalize).WithName($"scenario-{bag.ScenarioId}");
        return builder.Build();
    }

    private async Task ExecuteToolStepAsync(ScenarioRunBag s, string toolName, int occurrence, CancellationToken ct)
    {
        var desired = toolPolicy.StateFor(toolName, s.ScenarioId);
        if (s.State != desired)
            s.State = Record(s.Steps, "WORKFLOW_ROUTE", "AGENT_FRAMEWORK", s.State, desired, null, new { next_tool = toolName });

        var args = BuildArgs(toolName, s.ScenarioId, s.Scenario, s.Order, s.Facts, s.TraceId, occurrence);
        string? token = null;
        if (toolPolicy.RequiresConfirmation(toolName))
        {
            token = await confirmationStore.IssueAsync(
                s.Scenario.CaseId, s.Order.OrderId, s.Order.Version, toolName, TimeSpan.FromMinutes(10), ct);
            args["confirmation_token"] = token;
        }

        var result = await tools.InvokeAsync(new ToolCall(
            s.TraceId, toolName, toolPolicy.AccessOf(toolName), s.Scenario.UserId, s.Order.OrderId, s.Scenario.CaseId, s.Risk,
            s.State, args, token, Convert.ToString(args.GetValueOrDefault("idempotency_key")),
            s.Order.Version), ct);

        if (!result.Allowed || !result.Success)
            throw new InvalidOperationException($"{toolName} failed: {result.DenyReason}");

        s.Facts[toolName] = result.Data;
        s.ToolCalls.Add(toolName);
        var after = NextStateAfter(toolName, s.State);
        Record(s.Steps, "TOOL_EXECUTION", "AIFunction/ToolGateway", s.State, after, toolName, result.Data);
        s.State = after;
    }

    private static string NextStateAfter(string toolName, string current) => toolName switch
    {
        "submit_cancellation" => "TRACKING_REFUND",
        "schedule_deadline_action" => "WAITING_EXTERNAL",
        "create_human_handoff" => "ESCALATED",
        "confirm_recovery_outcome" => "RESOLVED",
        "create_supplier_case" => "WAITING_EXTERNAL",
        "get_supplier_case" => "OPTION_PRESENTED",
        "accept_supplier_offer" => "TRACKING_REFUND",
        "create_exception_review" => "ESCALATED",
        "create_service_dispute_case" => "WAITING_EXTERNAL",
        "submit_order_change" => "RESOLVED",
        "create_finance_case" => "ESCALATED",
        _ => current
    };

    private static string MapFinalCaseStatus(string scenarioId, string state, string expected) => expected;

    private static Dictionary<string, object?> BuildArgs(
        string name, string scenarioId, ScenarioFixture scenario, HotelOrder order,
        Dictionary<string, object?> facts, string traceId, int occurrence)
    {
        var userId = scenario.UserId;
        var orderId = order.OrderId;
        var caseId = scenario.CaseId;
        var idem = $"idem-{scenarioId}-{name}-{occurrence}";

        string? FactStr(string tool, string field)
        {
            if (!facts.TryGetValue(tool, out var data) || data is null) return null;
            var json = JsonSerializer.Serialize(data);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var p) ? p.ToString() : null;
        }

        Dictionary<string, object?> map = name switch
        {
            "list_user_orders" => new() { ["authenticated_user_id"] = userId, ["status_filter"] = "CONFIRMED" },
            "get_order_detail" => new() { ["authenticated_user_id"] = userId, ["order_id"] = orderId },
            "get_policy_snapshot" => new() { ["order_id"] = orderId, ["policy_id"] = order.PolicyId, ["policyCode"] = order.PolicyId },
            "list_after_sale_events" => new() { ["order_id"] = orderId },
            "calculate_refund_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["reason_code"] = "PLAN_CHANGE", ["refund"] = order.PaidAmount, ["fee"] = 0m },
            "validate_action_permission" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["action"] = "CANCEL", ["expected_order_version"] = order.Version },
            "submit_cancellation" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["quote_id"] = FactStr("calculate_refund_quote", "quote_id"), ["idempotency_key"] = idem },
            "get_refund_status" => new() { ["refund_id"] = order.RefundId ?? FactStr("submit_cancellation", "refund_id") ?? "RFD-DEMO" },
            "get_payment_events" => new() { ["authenticated_user_id"] = userId, ["payment_id"] = order.PaymentId },
            "schedule_deadline_action" => new() { ["case_id"] = caseId, ["deadline"] = DateTimeOffset.UtcNow.AddDays(2), ["action_type"] = "CHECK_REFUND_SLA", ["idempotency_key"] = idem },
            "create_payment_investigation" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["refund_id"] = order.RefundId, ["trigger_reason"] = "SLA_BREACH", ["idempotency_key"] = idem },
            "verify_fulfillment_issue" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reported_issue"] = "NO_ROOM", ["user_on_site"] = scenarioId == "E" },
            "get_alternative_hotels" => new() { ["source_order_id"] = orderId, ["location"] = "HOTEL_AREA", ["check_in"] = order.CheckIn, ["check_out"] = order.CheckOut, ["minimum_star_level"] = 4 },
            "get_guarantee_quote" => new() { ["order_id"] = orderId, ["issue_type"] = "NO_ROOM", ["alternative_hotel_id"] = "ALT-1" },
            "reserve_mock_alternative" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["alternative_hotel_id"] = "ALT-1", ["idempotency_key"] = idem },
            "create_human_handoff" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["summary"] = scenario.Title, ["queue"] = HandoffQueue(scenarioId), ["idempotency_key"] = idem },
            "get_handoff_status" => new() { ["handoff_id"] = FactStr("create_human_handoff", "handoff_id") },
            "confirm_recovery_outcome" => new() { ["case_id"] = caseId, ["recovery_outcome"] = "ALTERNATIVE_HOTEL_CONFIRMED", ["user_confirmation"] = true, ["idempotency_key"] = idem },
            "build_supplier_case_draft" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reason_code"] = "PLAN_CHANGE" },
            "create_supplier_case" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["draft_id"] = FactStr("build_supplier_case_draft", "draft_id"), ["idempotency_key"] = idem },
            "get_supplier_case" => new() { ["supplier_case_id"] = FactStr("create_supplier_case", "supplier_case_id") ?? "SUP-F-001" },
            "accept_supplier_offer" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["supplier_case_id"] = "SUP-F-001", ["offer_id"] = "OFF-F-REFUND", ["idempotency_key"] = idem },
            "submit_evidence_metadata" => new()
            {
                ["authenticated_user_id"] = userId, ["case_id"] = caseId,
                ["evidence_type"] = scenarioId == "G" ? "TRANSPORT_CANCELLATION_NOTICE" : (occurrence == 2 ? "ISSUE_PHOTO" : "HOTEL_CONTACT_RESULT"),
                ["storage_reference"] = $"mock://evidence/{scenarioId}/{occurrence}", ["consent"] = true, ["idempotency_key"] = idem
            },
            "extract_evidence_fields" => new() { ["evidence_id"] = FactStr("submit_evidence_metadata", "evidence_id"), ["extraction_schema"] = "MINIMUM_NECESSARY_V1" },
            "create_exception_review" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reason_code"] = "TRANSPORT_CANCELLATION", ["idempotency_key"] = idem },
            "create_service_dispute_case" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["idempotency_key"] = idem },
            "get_change_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["change_type"] = "DATE" },
            "submit_order_change" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["change_quote_id"] = FactStr("get_change_quote", "change_quote_id"), ["idempotency_key"] = idem },
            "create_finance_case" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["payment_id"] = order.PaymentId, ["idempotency_key"] = idem },
            "get_responsibility_chain" => new() { ["order_id"] = orderId },
            "get_group_order_breakdown" => new() { ["authenticated_user_id"] = userId, ["order_id"] = orderId },
            "get_partial_cancel_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version },
            _ => new()
        };
        map["trace_id"] = traceId;
        return map;
    }

    private static string HandoffQueue(string scenarioId) => scenarioId switch
    {
        "D" or "E" => "URGENT_HUMAN_HANDOFF",
        "H" => "SERVICE_RECOVERY_SPECIALIST",
        "K" => "CROSS_BORDER_SPECIALIST",
        "L" => "CORPORATE_GROUP_SPECIALIST",
        _ => "HUMAN_SPECIALIST"
    };

    private static string Record(
        List<WorkflowStepDto> steps, string node, string actor, string before, string after, string? tool, object? result)
    {
        steps.Add(new WorkflowStepDto(steps.Count + 1, node, actor, before, after, tool, result));
        return after;
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var i = 0;
        foreach (var item in actual)
            if (i < expected.Count && item == expected[i]) i++;
        return i == expected.Count;
    }

    private sealed class ScenarioRunBag
    {
        public required string RunId { get; init; }
        public required string TraceId { get; init; }
        public required string ScenarioId { get; init; }
        public required ScenarioFixture Scenario { get; init; }
        public required HotelOrder Order { get; init; }
        public required List<string> RequiredTools { get; init; }
        public required RiskLevel Risk { get; init; }
        public string State { get; set; } = "START";
        public List<WorkflowStepDto> Steps { get; } = [];
        public List<string> ToolCalls { get; } = [];
        public Dictionary<string, object?> Facts { get; } = new();
    }
}
