using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Abstractions.Ai;

/// <summary>
/// Ambient call context for MEAI <see cref="Microsoft.Extensions.AI.AIFunction"/> wrappers around <see cref="IToolGateway"/>.
/// </summary>
public sealed class ToolInvocationContext
{
    private static readonly AsyncLocal<ToolInvocationContext?> CurrentLocal = new();

    public static ToolInvocationContext? Current => CurrentLocal.Value;

    public required string TraceId { get; init; }
    public required string UserId { get; init; }
    public string? OrderId { get; init; }
    public string? CaseId { get; init; }
    public string? ScenarioId { get; init; }
    public required RiskLevel RiskLevel { get; init; }
    public required string ConversationState { get; init; }
    public IDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();
    public string? ConfirmationToken { get; init; }
    public string? IdempotencyKey { get; init; }
    public int? ExpectedOrderVersion { get; init; }
    public ToolAccess Access { get; init; } = ToolAccess.Read;

    /// <summary>Plugin-merged tool policy for this turn (set by conversation runtime).</summary>
    public IToolPolicy? Policy { get; init; }

    /// <summary>
    /// Builds a Gateway call using ambient <see cref="Policy"/> for access + per-tool conversation state.
    /// </summary>
    public ToolCall ToToolCall(string toolName)
    {
        var policy = Policy ?? NullToolPolicy.Instance;
        var access = policy.AccessOf(toolName);
        var state = policy.StateFor(toolName, ScenarioId);
        var idem = IdempotencyKey;
        if (policy.IsWrite(toolName) && string.IsNullOrWhiteSpace(idem))
            idem = $"agent-{toolName}-{Guid.NewGuid():N}"[..28];

        return new ToolCall(
            TraceId, toolName, access, UserId, OrderId, CaseId, RiskLevel, state,
            Arguments, ConfirmationToken, idem, ExpectedOrderVersion);
    }

    public static IDisposable Push(ToolInvocationContext context)
    {
        var prior = CurrentLocal.Value;
        CurrentLocal.Value = context;
        return new Popper(prior);
    }

    public static IDisposable Push(ToolCall call, IToolPolicy? policy = null) =>
        Push(new ToolInvocationContext
        {
            TraceId = call.TraceId,
            UserId = call.UserId,
            OrderId = call.OrderId,
            CaseId = call.CaseId,
            RiskLevel = call.RiskLevel,
            ConversationState = call.ConversationState,
            Arguments = call.Arguments,
            ConfirmationToken = call.ConfirmationToken,
            IdempotencyKey = call.IdempotencyKey,
            ExpectedOrderVersion = call.ExpectedOrderVersion,
            Access = call.Access,
            Policy = policy
        });

    private sealed class Popper(ToolInvocationContext? prior) : IDisposable
    {
        public void Dispose() => CurrentLocal.Value = prior;
    }
}

/// <summary>Fallback when no plugin policy is on the ambient context (all tools treated as read).</summary>
file sealed class NullToolPolicy : IToolPolicy
{
    public static readonly NullToolPolicy Instance = new();
    public bool IsWrite(string toolName) => false;
    public bool RequiresConfirmation(string toolName) => false;
    public ToolAccess AccessOf(string toolName) => ToolAccess.Read;
    public string StateFor(string toolName, string? scenarioId = null) => "DECISION_READY";
}
