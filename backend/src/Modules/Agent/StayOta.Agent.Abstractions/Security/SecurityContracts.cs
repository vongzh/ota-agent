using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Abstractions.Security;

/// <summary>Minimal caller identity for tool authorization (not a full IdP).</summary>
public sealed record CallerIdentity(
    string CallerId,
    IReadOnlySet<string> Roles,
    IReadOnlyDictionary<string, string>? Claims = null)
{
    public static CallerIdentity Anonymous { get; } = new("anonymous", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public static CallerIdentity DemoFullAccess { get; } = new(
        "demo",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "agent:read", "agent:write", "agent:admin" });
}

/// <summary>AsyncLocal carrier set by host middleware for the current request.</summary>
public static class CallerContext
{
    private static readonly AsyncLocal<CallerIdentity?> CurrentLocal = new();
    public static CallerIdentity Current => CurrentLocal.Value ?? CallerIdentity.Anonymous;

    public static IDisposable Push(CallerIdentity identity)
    {
        var prior = CurrentLocal.Value;
        CurrentLocal.Value = identity;
        return new Popper(prior);
    }

    private sealed class Popper(CallerIdentity? prior) : IDisposable
    {
        public void Dispose() => CurrentLocal.Value = prior;
    }
}

public interface IToolAuthorization
{
    /// <summary>Returns null when allowed; otherwise a deny reason.</summary>
    string? Authorize(CallerIdentity caller, string toolName, ToolAccess access);
}

public sealed record GuardrailContext(
    string? Surface,
    string? ToolName,
    ToolAccess? Access,
    string? UserMessage,
    CallerIdentity Caller,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record GuardrailResult(bool Allowed, string? DenyReason = null)
{
    public static GuardrailResult Allow() => new(true);
    public static GuardrailResult Deny(string reason) => new(false, reason);
}

public interface IGuardrail
{
    string Name { get; }
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default);
}

public interface IGuardrailPipeline
{
    Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default);
}

public interface IToolContractValidator
{
    /// <summary>Validate required inputs from the tool contract against call ambient + arguments.</summary>
    string? ValidateRequiredInputs(ToolContractDto contract, ToolCall call);
}
