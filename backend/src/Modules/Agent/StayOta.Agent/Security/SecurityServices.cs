using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Security;
using StayOta.Agent.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace StayOta.Agent.Security;

public sealed class GuardrailPipeline(IEnumerable<IGuardrail> guardrails, IOptions<GuardrailOptions> options)
    : IGuardrailPipeline
{
    public async Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        if (!options.Value.Enabled) return GuardrailResult.Allow();
        foreach (var g in guardrails)
        {
            var result = await g.EvaluateAsync(context, ct);
            if (!result.Allowed) return result;
        }

        return GuardrailResult.Allow();
    }
}

/// <summary>Blocks known jailbreak / exfil phrases in messages and string arguments.</summary>
public sealed class BlockedPhraseGuardrail(IOptions<GuardrailOptions> options) : IGuardrail
{
    public string Name => "blocked_phrase";

    public Task<GuardrailResult> EvaluateAsync(GuardrailContext context, CancellationToken ct = default)
    {
        var phrases = options.Value.BlockedPhrases;
        if (phrases.Length == 0) return Task.FromResult(GuardrailResult.Allow());

        if (ContainsBlocked(context.UserMessage, phrases))
            return Task.FromResult(GuardrailResult.Deny("guardrail: blocked phrase in user message"));

        if (context.Arguments is not null)
        {
            foreach (var kv in context.Arguments)
            {
                if (kv.Value is string s && ContainsBlocked(s, phrases))
                    return Task.FromResult(GuardrailResult.Deny($"guardrail: blocked phrase in argument '{kv.Key}'"));
            }
        }

        return Task.FromResult(GuardrailResult.Allow());
    }

    private static bool ContainsBlocked(string? text, string[] phrases)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var p in phrases)
        {
            if (!string.IsNullOrWhiteSpace(p) &&
                text.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public sealed class ClaimToolAuthorization(IOptions<ToolAuthOptions> options) : IToolAuthorization
{
    public string? Authorize(CallerIdentity caller, string toolName, ToolAccess access)
    {
        if (!options.Value.Enabled) return null;

        if (caller.Roles.Contains("agent:admin")) return null;

        var allow = options.Value.RoleToolAllowList;
        if (allow.Count > 0)
        {
            foreach (var role in caller.Roles)
            {
                if (allow.TryGetValue(role, out var tools) &&
                    tools.Any(t => string.Equals(t, toolName, StringComparison.Ordinal) || t == "*"))
                    return null;
            }

            return $"authorization denied for tool '{toolName}' (no matching role allow-list)";
        }

        if (access == ToolAccess.Write)
        {
            if (caller.Roles.Contains("agent:write") || caller.Roles.Contains("agent:admin"))
                return null;
            return $"authorization denied: write tool '{toolName}' requires agent:write";
        }

        if (caller.Roles.Contains("agent:read") || caller.Roles.Contains("agent:write") ||
            caller.Roles.Contains("agent:admin"))
            return null;

        return $"authorization denied: read tool '{toolName}' requires agent:read";
    }
}

public sealed class ToolContractValidator : IToolContractValidator
{
    /// <summary>MVP: only enforce identity/gate fields that map to ToolCall ambient (progressive).</summary>
    private static readonly HashSet<string> Enforceable = new(StringComparer.OrdinalIgnoreCase)
    {
        "authenticated_user_id", "user_id", "order_id", "case_id", "trace_id",
        "confirmation_token", "idempotency_key", "expected_order_version"
    };

    private static readonly Dictionary<string, Func<ToolCall, object?>> Ambient =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["authenticated_user_id"] = c => c.UserId,
            ["user_id"] = c => c.UserId,
            ["order_id"] = c => c.OrderId,
            ["case_id"] = c => c.CaseId,
            ["trace_id"] = c => c.TraceId,
            ["confirmation_token"] = c => c.ConfirmationToken,
            ["idempotency_key"] = c => c.IdempotencyKey,
            ["expected_order_version"] = c => c.ExpectedOrderVersion
        };

    public string? ValidateRequiredInputs(ToolContractDto contract, ToolCall call)
    {
        var required = contract.RequiredInputs;
        if (required is null || required.Count == 0) return null;

        var missing = new List<string>();
        foreach (var name in required)
        {
            if (!Enforceable.Contains(name)) continue;
            if (HasValue(call, name)) continue;
            missing.Add(name);
        }

        return missing.Count == 0
            ? null
            : $"contract validation failed: missing required input(s) [{string.Join(", ", missing)}]";
    }

    private static bool HasValue(ToolCall call, string name)
    {
        if (call.Arguments.TryGetValue(name, out var arg) && !IsEmpty(arg))
            return true;

        foreach (var kv in call.Arguments)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && !IsEmpty(kv.Value))
                return true;
        }

        if (Ambient.TryGetValue(name, out var getter))
        {
            var v = getter(call);
            return !IsEmpty(v);
        }

        return false;
    }

    private static bool IsEmpty(object? v) =>
        v is null || (v is string s && string.IsNullOrWhiteSpace(s));
}
