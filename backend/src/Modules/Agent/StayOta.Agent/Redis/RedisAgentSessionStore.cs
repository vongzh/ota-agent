using System.Text.Json;
using StackExchange.Redis;
using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Redis;

public sealed class RedisAgentSessionStore(IConnectionMultiplexer mux) : IAgentSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);
    private const string IndexKey = "agent:sessions:index";

    public async Task SaveAsync(string sessionId, AgentSessionSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        var db = mux.GetDatabase();
        var json = JsonSerializer.Serialize(snapshot);
        await db.StringSetAsync($"agent:session:{sessionId}", json, Ttl);
        await db.SortedSetAddAsync(IndexKey, sessionId, snapshot.UpdatedAt.ToUnixTimeSeconds());
    }

    public async Task<AgentSessionSnapshot?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var value = await db.StringGetAsync($"agent:session:{sessionId}");
        if (value.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AgentSessionSnapshot>((string)value!);
    }

    public async Task<IReadOnlyList<AgentSessionSummary>> ListAsync(int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var db = mux.GetDatabase();
        var ids = await db.SortedSetRangeByRankAsync(IndexKey, -take, -1, Order.Descending);
        var list = new List<AgentSessionSummary>(ids.Length);
        foreach (var id in ids)
        {
            if (id.IsNullOrEmpty) continue;
            var sessionId = (string)id!;
            var snap = await GetAsync(sessionId, ct);
            if (snap is null)
            {
                await db.SortedSetRemoveAsync(IndexKey, sessionId);
                continue;
            }

            list.Add(new AgentSessionSummary(
                sessionId,
                snap.TraceId,
                snap.UserId,
                snap.OrderId,
                snap.CaseId,
                snap.ScenarioId,
                snap.PendingApprovals.Count,
                snap.UpdatedAt));
        }

        return list;
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var removed = await db.KeyDeleteAsync($"agent:session:{sessionId}");
        await db.SortedSetRemoveAsync(IndexKey, sessionId);
        return removed;
    }
}
