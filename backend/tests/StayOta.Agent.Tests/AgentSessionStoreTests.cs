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
            _map[sessionId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<AgentSessionSnapshot?> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_map.TryGetValue(sessionId, out var s) ? s : null);
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
}
