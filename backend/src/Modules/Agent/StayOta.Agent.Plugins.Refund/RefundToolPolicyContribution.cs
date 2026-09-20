using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Refund;

/// <summary>Refund vertical tool write/confirm/state tables (plugin contribution).</summary>
public sealed class RefundToolPolicyContribution : ToolPolicyContribution
{
    public RefundToolPolicyContribution()
    {
        foreach (var t in new[]
                 {
                     "submit_cancellation", "schedule_deadline_action", "create_payment_investigation",
                     "reserve_mock_alternative", "create_human_handoff", "confirm_recovery_outcome",
                     "create_supplier_case", "accept_supplier_offer", "submit_evidence_metadata",
                     "create_exception_review", "create_service_dispute_case", "submit_order_change",
                     "create_finance_case"
                 })
            WriteTools.Add(t);

        foreach (var t in new[]
                 {
                     "submit_cancellation", "submit_order_change", "accept_supplier_offer", "reserve_mock_alternative"
                 })
            ConfirmRequired.Add(t);

        foreach (var (name, state) in new Dictionary<string, string>
                 {
                     ["list_user_orders"] = "INTENT_READY",
                     ["get_order_detail"] = "ORDER_CONFIRMED",
                     ["get_policy_snapshot"] = "ORDER_CONFIRMED",
                     ["list_after_sale_events"] = "FACTS_REQUIRED",
                     ["calculate_refund_quote"] = "DECISION_READY",
                     ["validate_action_permission"] = "DECISION_READY",
                     ["submit_cancellation"] = "CONFIRMATION_REQUIRED",
                     ["get_refund_status"] = "TRACKING_REFUND",
                     ["get_payment_events"] = "TRACKING_REFUND",
                     ["schedule_deadline_action"] = "TRACKING_REFUND",
                     ["create_payment_investigation"] = "WAITING_EXTERNAL",
                     ["verify_fulfillment_issue"] = "ORDER_CONFIRMED",
                     ["get_alternative_hotels"] = "DECISION_READY",
                     ["get_guarantee_quote"] = "DECISION_READY",
                     ["create_human_handoff"] = "OPTION_PRESENTED",
                     ["build_supplier_case_draft"] = "FACTS_REQUIRED",
                     ["create_supplier_case"] = "CONFIRMATION_REQUIRED",
                     ["submit_evidence_metadata"] = "FACTS_REQUIRED",
                     ["extract_evidence_fields"] = "FACTS_REQUIRED",
                     ["create_exception_review"] = "DECISION_READY",
                     ["create_service_dispute_case"] = "DECISION_READY",
                     ["get_change_quote"] = "DECISION_READY",
                     ["submit_order_change"] = "CONFIRMATION_REQUIRED",
                     ["create_finance_case"] = "DECISION_READY",
                     ["get_responsibility_chain"] = "DECISION_READY",
                     ["get_group_order_breakdown"] = "FACTS_REQUIRED",
                     ["get_partial_cancel_quote"] = "DECISION_READY",
                     ["get_supplier_case"] = "WAITING_EXTERNAL",
                     ["accept_supplier_offer"] = "OPTION_PRESENTED",
                     ["get_handoff_status"] = "ESCALATED",
                     ["confirm_recovery_outcome"] = "ESCALATED",
                     ["reserve_mock_alternative"] = "OPTION_PRESENTED",
                 })
            DefaultStates[name] = state;

        ScenarioOverrides[("C", "get_payment_events")] = "TRACKING_REFUND";
        ScenarioOverrides[("H", "create_human_handoff")] = "WAITING_EXTERNAL";
        ScenarioOverrides[("K", "create_human_handoff")] = "DECISION_READY";
        ScenarioOverrides[("E", "create_human_handoff")] = "OPTION_PRESENTED";
        ScenarioOverrides[("L", "create_human_handoff")] = "OPTION_PRESENTED";
        ScenarioOverrides[("D", "create_human_handoff")] = "OPTION_PRESENTED";
    }
}
