using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Plugins.Refund.Services;

public sealed class Verifier : IVerifier
{
    public VerificationResult VerifyDecision(AgentDecisionDto decision)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(decision.ScenarioId))
            violations.Add("missing scenario");

        // Approval continuation is a shorter turn — do not require full dialogue pipeline shape.
        if (string.Equals(decision.Intent, "function_approval", StringComparison.Ordinal))
        {
            if (decision.Steps.Count < 1)
                violations.Add("approval steps incomplete");
            if (decision.Order.Amount < 0)
                violations.Add("invalid order amount");
            if (decision.RiskLevel == RiskLevel.L3 &&
                decision.Action is "ConfirmCancel" or "AutoRefund" or "ChangeOrder")
                violations.Add("L3 must not auto-write financial actions");
            return new VerificationResult(violations.Count == 0, violations);
        }

        if (decision.Steps.Count < 5)
            violations.Add("decision steps incomplete");
        if (decision.PolicyMatches.Count == 0)
            violations.Add("missing policy matches");
        if (decision.RiskLevel == RiskLevel.L3 &&
            decision.Action is "ConfirmCancel" or "AutoRefund" or "ChangeOrder")
            violations.Add("L3 must not auto-write financial actions");
        if ((decision.Action is "ConfirmCancel" or "ChangeOrder") && decision.RefundAmount is null)
            violations.Add("financial action missing quote amount");
        if (decision.Order.Amount < 0)
            violations.Add("invalid order amount");
        if (decision.Action == "ExplainProgress" &&
            decision.Reply.Contains("已到账", StringComparison.Ordinal))
            violations.Add("must not claim refunded while channel processing");
        if (decision.AgentDriven && decision.ToolSequence.Count == 0 && !decision.HasPendingApprovals &&
            decision.Action is "ConfirmCancel" or "ExplainProgress" or "HumanHandoff")
            violations.Add("agent-driven turn produced no tools");
        return new VerificationResult(violations.Count == 0, violations);
    }

    public VerificationResult VerifyWorkflow(WorkflowRunResultDto run, IReadOnlyList<string> expectedTools, string expectedCaseStatus)
    {
        var violations = new List<string>();
        if (!IsSubsequence(expectedTools, run.ToolCalls))
            violations.Add("required tools not called in order");
        if (!StatusCompatible(run.CaseStatus, expectedCaseStatus))
            violations.Add($"case status expected {expectedCaseStatus} got {run.CaseStatus}");
        var allowedExtra = new HashSet<string> { "submit_evidence_metadata" };
        var unexpected = run.ToolCalls.Where(t => !expectedTools.Contains(t) && !allowedExtra.Contains(t)).ToList();
        if (unexpected.Count > 0)
            violations.Add($"unexpected tools: {string.Join(',', unexpected)}");
        if (run.Steps.Count == 0)
            violations.Add("empty workflow steps");
        return new VerificationResult(violations.Count == 0, violations);
    }

    public static bool StatusCompatible(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["REFUND_INITIATED"] = ["REFUND_INITIATED", "TRACKING_REFUND", "WAITING_USER_CONFIRM", "AWAITING_PAYMENT"],
            ["AWAITING_PAYMENT"] = ["AWAITING_PAYMENT", "WAITING_EXTERNAL", "TRACKING_REFUND"],
            ["RECOVERY_IN_PROGRESS"] = ["RECOVERY_IN_PROGRESS", "ESCALATED"],
            ["RECOVERED"] = ["RECOVERED", "RESOLVED", "ESCALATED"],
            ["MANUAL_REVIEW"] = ["MANUAL_REVIEW", "ESCALATED", "WAITING_EXTERNAL"],
            ["AWAITING_SPECIALIST"] = ["AWAITING_SPECIALIST", "ESCALATED"],
            ["CHANGED"] = ["CHANGED", "RESOLVED", "WAITING_USER_CONFIRM"],
        };
        return map.TryGetValue(expected, out var ok) && ok.Contains(actual, StringComparer.OrdinalIgnoreCase);
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
