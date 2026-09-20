using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Security;

namespace StayOta.Agent.Host.Security;

/// <summary>
/// Sets <see cref="CallerContext"/> from <c>X-Caller-Id</c> / <c>X-Caller-Roles</c>
/// (comma-separated). Defaults to demo full access when auth is off, or read+write when API key present.
/// </summary>
public sealed class CallerIdentityMiddleware(RequestDelegate next, IOptions<HostingOptions> hosting)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var callerId = context.Request.Headers["X-Caller-Id"].FirstOrDefault();
        var rolesHeader = context.Request.Headers["X-Caller-Roles"].FirstOrDefault();

        CallerIdentity identity;
        if (!string.IsNullOrWhiteSpace(callerId) || !string.IsNullOrWhiteSpace(rolesHeader))
        {
            var roles = (rolesHeader ?? "agent:read,agent:write")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            identity = new CallerIdentity(
                string.IsNullOrWhiteSpace(callerId) ? "header-caller" : callerId!,
                roles);
        }
        else if (string.IsNullOrWhiteSpace(hosting.Value.ApiKey))
        {
            identity = CallerIdentity.DemoFullAccess;
        }
        else
        {
            identity = new CallerIdentity(
                "api-key",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "agent:read", "agent:write" });
        }

        using var _ = CallerContext.Push(identity);
        await next(context);
    }
}

/// <summary>Fixed-window rate limiter keyed by API key or remote IP.</summary>
public sealed class RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
{
    private static readonly ConcurrentDictionary<string, WindowCounter> Windows = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        var opts = options.Value;
        if (!opts.Enabled || opts.RequestsPerMinute <= 0)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger"))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "anon";
        var now = DateTimeOffset.UtcNow;
        var window = Windows.GetOrAdd(key, _ => new WindowCounter());
        if (!window.TryAdmit(now, opts.WindowSeconds, opts.RequestsPerMinute, out var retryAfter))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)retryAfter.TotalSeconds).ToString();
            await context.Response.WriteAsJsonAsync(new { message = "rate limit exceeded", retryAfterSeconds = retryAfter.TotalSeconds });
            return;
        }

        await next(context);
    }

    private sealed class WindowCounter
    {
        private readonly object _gate = new();
        private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
        private int _count;

        public bool TryAdmit(DateTimeOffset now, int windowSeconds, int limit, out TimeSpan retryAfter)
        {
            lock (_gate)
            {
                if (_windowStart == DateTimeOffset.MinValue ||
                    now - _windowStart >= TimeSpan.FromSeconds(windowSeconds))
                {
                    _windowStart = now;
                    _count = 0;
                }

                if (_count >= limit)
                {
                    retryAfter = TimeSpan.FromSeconds(windowSeconds) - (now - _windowStart);
                    if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.FromSeconds(1);
                    return false;
                }

                _count++;
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }
}
