using System.Text.Json;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Refund.Services;
// IToolPolicy injected — plugin contributions, not static ToolPolicy.

public interface IRefundDataStore
{
    Task EnsureSeededAsync(CancellationToken ct = default);
    Task ResetDemoAsync(CancellationToken ct = default);
    ScenarioFixture GetScenario(string scenarioId);
    IReadOnlyList<ScenarioFixture> ListScenarios();
    Task<HotelOrder?> GetOrderAsync(string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<HotelOrder>> ListOrdersAsync(string userId, CancellationToken ct = default);
    Task<PolicySnapshot?> GetPolicyAsync(string policyId, CancellationToken ct = default);
    Task<RefundCase> UpsertCaseAsync(RefundCase refundCase, CancellationToken ct = default);
    Task AppendEventAsync(string caseId, string eventType, object payload, CancellationToken ct = default);
    Task SaveWorkflowRunAsync(WorkflowRun run, CancellationToken ct = default);
    Task SaveToolAuditAsync(ToolAuditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<ToolAuditLog>> QueryToolAuditsAsync(string? traceId, string? caseId, int take = 50, CancellationToken ct = default);
    IReadOnlyList<ToolContractDto> GetToolContracts();
}

public sealed class ScenarioCatalog(IRefundDataStore store) : IScenarioCatalog
{
    public IReadOnlyList<ScenarioDto> List() =>
        store.ListScenarios().Select(s => new ScenarioDto(
            s.ScenarioId, s.Title, s.Group, s.Goal, s.EntryMessage, s.RiskLevel, s.ExpectedRoute,
            JsonSerializer.Deserialize<List<string>>(s.RequiredToolsJson) ?? [])).ToList();

    public ScenarioFixture GetRequired(string scenarioId) => store.GetScenario(scenarioId.ToUpperInvariant());
}

public sealed class ToolGateway(
    IRefundDataStore store,
    IConfirmationStore confirmationStore,
    IIdempotencyStore idempotencyStore,
    IToolPolicy toolPolicy,
    ILogger<ToolGateway> logger) : IToolGateway
{
    public IReadOnlyList<ToolContractDto> ListContracts() => store.GetToolContracts();

    public async Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default)
    {
        var contracts = store.GetToolContracts();
        if (contracts.All(c => c.Name != call.ToolName))
            return await Audit(call, false, false, null, $"unknown tool: {call.ToolName}", ct);

        if (string.IsNullOrWhiteSpace(call.UserId))
            return await Audit(call, false, false, null, "missing user identity", ct);

        var contract = contracts.First(c => c.Name == call.ToolName);
        if (contract.AllowedConversationStates.Count > 0 &&
            !string.IsNullOrWhiteSpace(call.ConversationState) &&
            !contract.AllowedConversationStates.Contains(call.ConversationState))
        {
            return await Audit(call, false, false, null,
                $"tool {call.ToolName} not allowed in state {call.ConversationState}", ct);
        }

        var isWrite = toolPolicy.IsWrite(call.ToolName);

        if (isWrite && call.RiskLevel == RiskLevel.L3 && call.ToolName is "submit_cancellation" or "submit_order_change")
            return await Audit(call, false, false, null, "L3 blocks auto financial write; escalate", ct);

        if (isWrite && toolPolicy.RequiresConfirmation(call.ToolName))
        {
            if (string.IsNullOrWhiteSpace(call.ConfirmationToken) || call.ExpectedOrderVersion is null ||
                string.IsNullOrWhiteSpace(call.OrderId) || string.IsNullOrWhiteSpace(call.CaseId))
                return await Audit(call, false, false, null, "confirmation/version/case/order required", ct);

            var ok = await confirmationStore.ConsumeAsync(
                call.ConfirmationToken!, call.CaseId!, call.OrderId!, call.ExpectedOrderVersion.Value, call.ToolName, ct);
            if (!ok)
                return await Audit(call, false, false, null, "invalid or expired confirmation token", ct);
        }

        var idemTtl = TimeSpan.FromHours(24);
        var hasIdem = isWrite && !string.IsNullOrWhiteSpace(call.IdempotencyKey);
        if (hasIdem)
        {
            var began = await idempotencyStore.TryBeginAsync(call.IdempotencyKey!, idemTtl, ct);
            if (!began)
            {
                var cached = await idempotencyStore.TryGetCompletedAsync(call.IdempotencyKey!, ct);
                if (cached is not null)
                {
                    object? replay;
                    try { replay = JsonSerializer.Deserialize<JsonElement>(cached); }
                    catch { replay = cached; }
                    return await Audit(call, true, true, new { duplicate = true, replay }, null, ct);
                }

                return await Audit(call, true, true, new { duplicate = true, in_flight = true }, null, ct);
            }
        }

        object? data;
        try
        {
            data = await ExecuteTool(call, ct);
        }
        catch
        {
            if (hasIdem)
                await idempotencyStore.AbandonAsync(call.IdempotencyKey!, ct);
            throw;
        }

        if (hasIdem)
            await idempotencyStore.CompleteAsync(call.IdempotencyKey!, JsonSerializer.Serialize(data ?? new { }), idemTtl, ct);

        logger.LogInformation("Tool {Tool} ok trace={Trace}", call.ToolName, call.TraceId);
        return await Audit(call, true, true, data, null, ct);
    }

    private async Task<object?> ExecuteTool(ToolCall call, CancellationToken ct)
    {
        var order = string.IsNullOrWhiteSpace(call.OrderId) ? null : await store.GetOrderAsync(call.OrderId!, ct);
        return call.ToolName switch
        {
            "list_user_orders" => new { orders = await store.ListOrdersAsync(call.UserId, ct) },
            "get_order_detail" => order,
            "get_policy_snapshot" => order is null ? null : await store.GetPolicyAsync(order.PolicyId, ct),
            "list_after_sale_events" => new
            {
                events = new[]
                {
                    new { event_id = "EVT-1", event_type = "CASE_OPENED", actor = "agent", occurred_at = DateTimeOffset.UtcNow }
                }
            },
            "calculate_refund_quote" => new
            {
                quote_id = $"Q-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                expires_at = DateTimeOffset.UtcNow.AddMinutes(10),
                paid_amount = order?.PaidAmount,
                refund_amount = ArgDec(call, "refund") ?? order?.PaidAmount,
                cancellation_fee = ArgDec(call, "fee") ?? 0m,
                currency = order?.Currency ?? "CNY",
                refund_route = "original",
                estimated_arrival = DateTimeOffset.UtcNow.AddDays(3),
                policy_id = order?.PolicyId,
                requires_human = call.RiskLevel != RiskLevel.L1
            },
            "validate_action_permission" => new
            {
                allowed = call.RiskLevel != RiskLevel.L3 || call.ToolName != "submit_cancellation",
                risk_level = call.RiskLevel.ToString(),
                denial_reasons = Array.Empty<string>(),
                confirmation_required = true
            },
            "submit_cancellation" => new
            {
                action_id = $"ACT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                order_status = "CANCELLED",
                new_order_version = (order?.Version ?? 1) + 1,
                refund_id = $"RFD-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                refund_status = "REFUND_INITIATED"
            },
            "get_action_result" => await ResolveActionResultAsync(call, ct),
            "get_refund_status" => new
            {
                refund_id = order?.RefundId ?? "RFD-DEMO",
                status = "CHANNEL_PROCESSING",
                amount = order?.PaidAmount,
                currency = order?.Currency,
                initiated_at = DateTimeOffset.UtcNow.AddDays(-3),
                channel = "UNIONPAY",
                sla_due_at = DateTimeOffset.UtcNow.AddDays(2),
                waiting_for = "PAYMENT_CHANNEL",
                timeline = new[] { "REFUND_INITIATED", "CHANNEL_PROCESSING" }
            },
            "get_payment_events" => new
            {
                payment_id = order?.PaymentId,
                masked_instrument = "**** 8888",
                events = new object[]
                {
                    new { event_id = "PE1", type = "CAPTURE", amount = order?.PaidAmount, status = "SUCCESS" },
                    new { event_id = "PE2", type = "PREAUTH_HOLD", amount = 500m, status = "HOLDING" }
                }
            },
            "schedule_deadline_action" => new { schedule_id = $"SCH-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), status = "SCHEDULED", deadline = DateTimeOffset.UtcNow.AddDays(2), action_type = "PAYMENT_CHECK" },
            "create_payment_investigation" => new { external_case_id = $"PAY-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), status = "OPEN", waiting_for = "PAYMENT_OPS", response_due_at = DateTimeOffset.UtcNow.AddDays(1) },
            "verify_fulfillment_issue" => new { verification_status = "CONFIRMED", confirmed_facts = new[] { "no_room_or_price_increase" }, unconfirmed_facts = Array.Empty<string>(), hotel_contact_status = "REACHED" },
            "get_alternative_hotels" => new
            {
                alternatives = new[]
                {
                    new { hotel_id = "ALT-1", distance_km = 1.2, available = true, nightly_price = 520m, currency = "CNY", price_difference = 40m, mock_hold_minutes = 15 }
                }
            },
            "get_guarantee_quote" => new { eligible = true, maximum_supported_amount = 140m, currency = "CNY", requires_human_confirmation = true, policy_id = order?.PolicyId },
            "reserve_mock_alternative" => new { hold_id = $"HOLD-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), expires_at = DateTimeOffset.UtcNow.AddMinutes(15), status = "HELD" },
            "create_human_handoff" => new
            {
                handoff_id = $"HO-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                status = "QUEUED",
                queue = call.RiskLevel == RiskLevel.L3 ? "urgent" : "specialist",
                response_due_at = DateTimeOffset.UtcNow.AddMinutes(5),
                context_digest = call.Arguments.TryGetValue("summary", out var s) ? s : "structured handoff"
            },
            "get_handoff_status" => new { status = "ASSIGNED", queue = "urgent", assigned_role = "onsite_specialist", response_due_at = DateTimeOffset.UtcNow.AddMinutes(3), latest_public_update = "专员处理中" },
            "confirm_recovery_outcome" => new { case_status = "RECOVERED", resolved_goal = "tonight_stay", remaining_tasks = new[] { "refund_liability" } },
            "build_supplier_case_draft" => new { draft_id = $"DRF-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), summary = "例外退款协商草案", requested_outcome = "partial_refund", missing_fields = Array.Empty<string>(), expires_at = DateTimeOffset.UtcNow.AddHours(2) },
            "create_supplier_case" => new { supplier_case_id = "SUP-F-001", status = "SUBMITTED", waiting_for = "HOTEL", response_due_at = DateTimeOffset.UtcNow.AddDays(1) },
            "get_supplier_case" => new { status = "OFFERED", waiting_for = "USER", response_due_at = DateTimeOffset.UtcNow.AddDays(1), offers = new[] { new { offer_id = "OFF-F-REFUND", amount = 1000m } } },
            "accept_supplier_offer" => new { action_id = "ACT-OFFER", accepted_offer_id = "OFF-F-REFUND", resulting_case_status = "REFUND_INITIATED", refund_id_or_change_id = "RFD-F-001" },
            "submit_evidence_metadata" => new { evidence_id = $"EV-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), accepted = true, retained_fields = new[] { "type", "reference" }, discarded_fields = new[] { "raw_bytes" } },
            "extract_evidence_fields" => new { fields = new { flight_no = "MU****", cancel_time = DateTimeOffset.UtcNow }, confidence_by_field = new { flight_no = 0.9 }, requires_user_confirmation = true, requires_human_review = false },
            "create_exception_review" => new { review_case_id = "REV-G-001", status = "OPEN", response_due_at = DateTimeOffset.UtcNow.AddDays(1), possible_outcomes = new[] { "full_refund", "partial_refund", "reject" } },
            "create_service_dispute_case" => new { dispute_case_id = $"DSP-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), status = "OPEN", waiting_for = "QA", response_due_at = DateTimeOffset.UtcNow.AddHours(8) },
            "get_change_quote" => new { change_quote_id = $"CQ-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), expires_at = DateTimeOffset.UtcNow.AddMinutes(10), change_allowed = true, price_difference = 80m, currency = "CNY", new_order_summary = "check_in+1", cancellation_comparison = 300m, requires_human = false },
            "submit_order_change" => new { change_id = $"CHG-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), status = "APPLIED", new_order_version = (order?.Version ?? 1) + 1, new_order_summary = "date changed", additional_payment_status = "REQUIRED_80" },
            "create_finance_case" => new { finance_case_id = "FIN-J-001", status = "OPEN", waiting_for = "FINANCE", response_due_at = DateTimeOffset.UtcNow.AddDays(1) },
            "get_responsibility_chain" => new { merchant_of_record = "SUP-OVERSEA-1", supplier_chain = new[] { "SUP-OVERSEA-1", "SUP-OVERSEA-2" }, refund_executor = "SUP-OVERSEA-1", transaction_currency = order?.Currency ?? "USD", hotel_timezone = "Asia/Tokyo", customer_facing_summary = "平台受理，海外供应商执行退款" },
            "get_group_order_breakdown" => new { room_items = Enumerable.Range(1, order?.RoomCount ?? 8).Select(i => new { room_item_id = $"RM-{i}", status = "CONFIRMED" }), payer = "示例科技有限公司", authorized_roles = new[] { "TRAVEL_MANAGER" }, operator_authorized = true, invoice_status = "ISSUED" },
            "get_partial_cancel_quote" => new { quote_id = $"PQ-{Guid.NewGuid():N}"[..10].ToUpperInvariant(), refund_amount = 3000m, cancellation_fee = 300m, discount_reallocation = 100m, invoice_impact = "need_reissue", requires_specialist = true },
            _ => new { ok = true }
        };
    }

    private async Task<object?> ResolveActionResultAsync(ToolCall call, CancellationToken ct)
    {
        var key = call.IdempotencyKey
                  ?? (call.Arguments.TryGetValue("idempotency_key", out var v) ? Convert.ToString(v) : null);
        if (string.IsNullOrWhiteSpace(key))
            return new { found = false, action_status = "UNKNOWN", business_result = (object?)null };

        var cached = await idempotencyStore.TryGetCompletedAsync(key!, ct);
        if (cached is null)
            return new { found = false, action_status = "IN_FLIGHT_OR_MISSING", business_result = (object?)null };

        object? body;
        try { body = JsonSerializer.Deserialize<JsonElement>(cached); }
        catch { body = cached; }
        return new { found = true, action_status = "SUCCEEDED", business_result = body };
    }

    private async Task<ToolResult> Audit(ToolCall call, bool allowed, bool success, object? data, string? deny, CancellationToken ct)
    {
        await store.SaveToolAuditAsync(new ToolAuditLog
        {
            TraceId = call.TraceId,
            CaseId = call.CaseId,
            ToolName = call.ToolName,
            Access = call.Access,
            Allowed = allowed,
            RequestJson = JsonSerializer.Serialize(call.Arguments),
            ResponseJson = JsonSerializer.Serialize(data ?? new { }),
            DenyReason = deny,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        return new ToolResult(allowed, success && allowed, call.ToolName, data, deny);
    }

    private static decimal? ArgDec(ToolCall call, string key) =>
        call.Arguments.TryGetValue(key, out var v) && v is not null ? Convert.ToDecimal(v) : null;
}
