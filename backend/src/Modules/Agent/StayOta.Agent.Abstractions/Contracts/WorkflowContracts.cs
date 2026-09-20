namespace StayOta.Agent.Abstractions.Contracts;

public sealed record ToolContractDto(
    string Name,
    string Mode,
    string Purpose,
    IReadOnlyList<string> AllowedConversationStates,
    IReadOnlyList<string>? RequiredInputs = null);


public sealed record WorkflowStepDto(
    int SequenceNo,
    string WorkflowNode,
    string Actor,
    string StateBefore,
    string StateAfter,
    string? ToolName,
    object? Result);

public sealed record WorkflowAssertionDto(
    bool RequiredToolsCalledInOrder,
    bool ExpectedCaseStatusReached,
    bool NoUnexpectedTool,
    bool VerifierPassed);

public sealed record WorkflowRunResultDto(
    string RunId,
    string ScenarioId,
    string Route,
    string CaseId,
    string FinalState,
    string CaseStatus,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<WorkflowStepDto> Steps,
    WorkflowAssertionDto Assertions,
    bool Succeeded);

public interface IScenarioWorkflow
{
    Task<WorkflowRunResultDto> RunAsync(string scenarioId, CancellationToken ct = default);
}

public interface IVerifier
{
    VerificationResult VerifyDecision(AgentDecisionDto decision);
    VerificationResult VerifyWorkflow(WorkflowRunResultDto run, IReadOnlyList<string> expectedTools, string expectedCaseStatus);
}

public sealed record VerificationResult(bool Passed, IReadOnlyList<string> Violations);
