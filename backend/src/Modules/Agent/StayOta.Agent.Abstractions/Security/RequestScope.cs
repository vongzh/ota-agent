namespace StayOta.Agent.Abstractions.Security;

/// <summary>
/// Lightweight demo isolation scope (storage key prefix only — not IdP / authz).
/// Set via <c>X-Scope-Id</c> header for multi-customer demos sharing one Host.
/// </summary>
public sealed class RequestScope
{
    public static RequestScope Empty { get; } = new(null);

    public RequestScope(string? scopeId)
    {
        ScopeId = Sanitize(scopeId);
    }

    /// <summary>Sanitized scope id, or null when unset.</summary>
    public string? ScopeId { get; }

    /// <summary>Redis / cache key prefix ending with <c>:</c>, or empty when unset.</summary>
    public string KeyPrefix =>
        string.IsNullOrEmpty(ScopeId) ? "" : $"scope:{ScopeId}:";

    private static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        // Keep keys Redis-safe and short for demos.
        var cleaned = new string(trimmed
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            .Take(64)
            .ToArray());
        return string.IsNullOrEmpty(cleaned) ? null : cleaned.ToLowerInvariant();
    }
}

/// <summary>Ambient request scope (AsyncLocal), set by host middleware.</summary>
public static class RequestScopeContext
{
    private static readonly AsyncLocal<RequestScope?> Current = new();

    public static RequestScope CurrentScope => Current.Value ?? RequestScope.Empty;

    public static IDisposable Push(RequestScope scope)
    {
        var prior = Current.Value;
        Current.Value = scope;
        return new Pop(prior);
    }

    private sealed class Pop(RequestScope? prior) : IDisposable
    {
        public void Dispose() => Current.Value = prior;
    }
}
