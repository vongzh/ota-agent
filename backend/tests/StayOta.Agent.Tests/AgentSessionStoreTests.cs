using System.Collections.Concurrent;
using StayOta.Agent.Abstractions.Ai;
using Xunit;

namespace StayOta.Agent.Tests;

public class AgentSessionStoreTests
{
    private sealed class MemoryAgentSessionStore : IAgentSessionStore
    {
        private readonly ConcurrentDictionary<string, AgentSessionSnapshot> _map = new();

        public Task SaveAsync(string sessionId, AgentSessionSnapshot snapshot, CancellationToken ct = default)
        {
            snapshot.UpdatedAt = DateTimeOffset.UtcNow;
            _map[sessionId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<AgentSessionSnapshot?> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_map.TryGetValue(sessionId, out var s) ? s : null);

        public Task<IReadOnlyList<AgentSessionSummary>> ListAsync(int take = 50, CancellationToken ct = default)
        {
            var list = _map
                .OrderByDescending(kv => kv.Value.UpdatedAt)
                .Take(Math.Clamp(take, 1, 200))
                .Select(kv => new AgentSessionSummary(
                    kv.Key, kv.Value.TraceId, kv.Value.UserId, kv.Value.OrderId, kv.Value.CaseId,
                    kv.Value.ScenarioId, kv.Value.PendingApprovals.Count, kv.Value.UpdatedAt))
                .ToList();
            return Task.FromResult<IReadOnlyList<AgentSessionSummary>>(list);
        }

        public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_map.TryRemove(sessionId, out _));
    }

    [Fact]
    public async Task SaveAndResume_PreservesSessionJson()
    {
        var store = new MemoryAgentSessionStore();
        var id = "ags_test_session_01";
        await store.SaveAsync(id, new AgentSessionSnapshot
        {
            SessionJson = """{"messages":[]}""",
            TraceId = "trc_1",
            UserId = "USR-A",
            OrderId = "ORD-A-001",
            CaseId = "CASE-A-001",
            ScenarioId = "A",
            ConversationState = "CONFIRMATION_REQUIRED"
        });

        var loaded = await store.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("ORD-A-001", loaded!.OrderId);
        Assert.Equal("CONFIRMATION_REQUIRED", loaded.ConversationState);
        Assert.Contains("messages", loaded.SessionJson);
    }

    [Fact]
    public async Task ListAndDelete_ManageSessionLifecycle()
    {
        var store = new MemoryAgentSessionStore();
        await store.SaveAsync("ags_a", new AgentSessionSnapshot
        {
            SessionJson = "{}",
            TraceId = "trc_a",
            UserId = "u1",
            OrderId = "o1",
            CaseId = "c1",
            ScenarioId = "A"
        });
        await store.SaveAsync("ags_b", new AgentSessionSnapshot
        {
            SessionJson = "{}",
            TraceId = "trc_b",
            UserId = "u2",
            OrderId = "o2",
            CaseId = "c2",
            ScenarioId = "B",
            PendingApprovals = [new PendingApprovalRecord { RequestId = "r1", ToolName = "submit_cancellation" }]
        });

        var listed = await store.ListAsync(10);
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, s => s.SessionId == "ags_b" && s.PendingApprovalCount == 1);

        Assert.True(await store.DeleteAsync("ags_a"));
        Assert.Null(await store.GetAsync("ags_a"));
        Assert.Single(await store.ListAsync());
    }
}

public class ToolAuditQueryTests
{
    [Fact]
    public async Task QueryToolAudits_FiltersByTraceAndCase()
    {
        var store = new MemoryRefundDataStore();
        await store.SaveToolAuditAsync(new StayOta.Agent.Abstractions.Domain.Entities.ToolAuditLog
        {
            TraceId = "trc_x",
            CaseId = "CASE-A",
            ToolName = "get_order_detail",
            Allowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.SaveToolAuditAsync(new StayOta.Agent.Abstractions.Domain.Entities.ToolAuditLog
        {
            TraceId = "trc_y",
            CaseId = "CASE-B",
            ToolName = "submit_cancellation",
            Allowed = false,
            DenyReason = "denied",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var byTrace = await store.QueryToolAuditsAsync("trc_x", null);
        Assert.Single(byTrace);
        Assert.Equal("get_order_detail", byTrace[0].ToolName);

        var byCase = await store.QueryToolAuditsAsync(null, "CASE-B");
        Assert.Single(byCase);
        Assert.Equal("CASE-B", byCase[0].CaseId);
    }
}
