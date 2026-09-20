using StackExchange.Redis;
using StayOta.Agent.Abstractions.Contracts;

namespace StayOta.Agent.Redis;

public sealed class RedisConfirmationStore(IConnectionMultiplexer mux) : IConfirmationStore
{
    public async Task<string> IssueAsync(string caseId, string orderId, int version, string action, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = $"cfm_{Guid.NewGuid():N}";
        var db = mux.GetDatabase();
        var payload = $"{caseId}|{orderId}|{version}|{action}";
        await db.StringSetAsync(Key(token), payload, ttl);
        return token;
    }

    public async Task<bool> ConsumeAsync(string token, string caseId, string orderId, int version, string action, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var key = Key(token);
        // Atomic get-and-delete to prevent double-spend under concurrency.
        var value = await db.StringGetDeleteAsync(key);
        if (value.IsNullOrEmpty) return false;
        var expected = $"{caseId}|{orderId}|{version}|{action}";
        if (string.Equals(value.ToString(), expected, StringComparison.Ordinal))
            return true;

        // Wrong binding — restore token so a mismatched probe does not burn a valid confirmation.
        await db.StringSetAsync(key, value, TimeSpan.FromMinutes(10));
        return false;
    }

    private static string Key(string token) => $"refund:confirm:{token}";
}

public sealed class RedisIdempotencyStore(IConnectionMultiplexer mux) : IIdempotencyStore
{
    private const string PendingMarker = "{\"s\":\"pending\"}";

    public async Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        return await db.StringSetAsync(RedisKey(key), PendingMarker, ttl, When.NotExists);
    }

    public async Task CompleteAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var wrapped = $"{{\"s\":\"ok\",\"d\":{responseJson}}}";
        await db.StringSetAsync(RedisKey(key), wrapped, ttl);
    }

    public async Task<string?> TryGetCompletedAsync(string key, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var value = await db.StringGetAsync(RedisKey(key));
        if (value.IsNullOrEmpty) return null;
        var text = value.ToString();
        if (text.StartsWith("{\"s\":\"ok\"", StringComparison.Ordinal))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("d", out var d))
                return d.GetRawText();
        }

        // Legacy marker "1" or pending — no replayable body.
        return null;
    }

    public async Task AbandonAsync(string key, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var redisKey = RedisKey(key);
        var value = await db.StringGetAsync(redisKey);
        if (value.IsNullOrEmpty) return;
        var text = value.ToString();
        // Only drop in-flight / legacy markers — never erase a completed payload.
        if (text == PendingMarker || text == "1")
            await db.KeyDeleteAsync(redisKey);
    }

    private static string RedisKey(string key) => $"refund:idem:{key}";
}

public sealed class RedisSessionStore(IConnectionMultiplexer mux) : ISessionStore
{
    public Task SetAsync(string sessionId, string json, TimeSpan ttl, CancellationToken ct = default) =>
        mux.GetDatabase().StringSetAsync($"refund:session:{sessionId}", json, ttl);

    public async Task<string?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var value = await mux.GetDatabase().StringGetAsync($"refund:session:{sessionId}");
        return value.IsNullOrEmpty ? null : value.ToString();
    }
}
