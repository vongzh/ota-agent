using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Ai;
using StayOta.Agent.Plugins;
using StayOta.Agent.Plugins.Refund;
using StayOta.Agent.Plugins.Refund.Ai;
using StayOta.Agent.Plugins.Refund.Production;
using StayOta.Agent.Plugins.Refund.Services;
using StayOta.Agent.Tools;
using Xunit;

namespace StayOta.Agent.Tests;

/// <summary>
/// End-to-end agent-first path without Postgres/Redis: Orchestrator → hints → Agent → Gateway.
/// </summary>
public class AgentFirstIntegrationTests
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

    private sealed class MemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, string> _map = new();

        public Task SetAsync(string sessionId, string json, TimeSpan ttl, CancellationToken ct = default)
        {
            _map[sessionId] = json;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_map.TryGetValue(sessionId, out var v) ? v : null);
    }

    private static AgentOrchestrator CreateOrchestrator(
        out MemoryRefundDataStore store,
        out MemoryAgentSessionStore agentSessions)
    {
        TestPaths.EnsureRootEnv();
        store = new MemoryRefundDataStore();
        var confirm = new MemoryConfirmationStore();
        var idem = new MemoryIdempotencyStore();
        var policy = new CompositeToolPolicy([new RefundToolPolicyContribution()]);
        var gateway = new ToolGateway(store, confirm, idem, policy, NullLogger<ToolGateway>.Instance);
        var catalog = new RefundAiToolCatalog(gateway, store, policy, new RefundToolFailureReplanner());
        agentSessions = new MemoryAgentSessionStore();
        var turn = new DeterministicTurnContext();
        var hitl = new TurnHitlOptions();
        var planner = new CompositeDeterministicIntentPlanner([new RefundDeterministicIntentPlanner()]);
        var chat = new DeterministicChatClient(turn, planner);
        var plugin = new RefundAgentPlugin();
        var echo = new StayOta.Agent.Plugins.Echo.EchoAgentPlugin();
        var registry = new AgentPluginRegistry([plugin, echo]);
        var services = new ServiceCollection().BuildServiceProvider();
        var composite = new CompositeAgentToolCatalog(
        [
            catalog,
            new StayOta.Agent.Plugins.Echo.EchoAiToolCatalog()
        ]);
        var host = new ChatClientAgentHost(chat, composite, registry, hitl, NullLoggerFactory.Instance, services);
        var conversation = new AgentConversationService(
            host, agentSessions, turn, hitl, policy, NullLogger<AgentConversationService>.Instance);
        var production = new MockProductionOrderClient(store);
        var hosting = Options.Create(new HostingOptions { DemoEnabled = true });

        return new AgentOrchestrator(
            store,
            new IntentService(),
            new PolicyRetrieval(),
            new RulesEngine(),
            host,
            conversation,
            agentSessions,
            confirm,
            new MemorySessionStore(),
            new Verifier(),
            production,
            policy,
            hosting,
            NullLogger<AgentOrchestrator>.Instance);
    }

    [Fact]
    public async Task HandleAsync_ScenarioA_AgentInvokesHintTools()
    {
        var orch = CreateOrchestrator(out _, out _);
        var decision = await orch.HandleAsync(new AgentMessageRequest(
            "明天入住的酒店现在取消，页面显示可以免费取消。",
            ScenarioId: "A"));

        Assert.True(decision.AgentDriven);
        Assert.Equal("A", decision.ScenarioId);
        Assert.Equal("ConfirmCancel", decision.Action);
        Assert.True(decision.VerificationPassed, string.Join("; ", decision.VerificationViolations));
        Assert.Contains("get_order_detail", decision.ToolSequence);
        Assert.Contains("calculate_refund_quote", decision.ToolSequence);
        Assert.DoesNotContain("submit_cancellation", decision.ToolSequence);
        Assert.True(decision.HasPendingApprovals);
        Assert.NotNull(decision.AgentSessionId);
        Assert.Contains(decision.PendingApprovals ?? [], p => p.ToolName == "submit_cancellation");
    }

    [Fact]
    public async Task HandleAsync_ThenApprove_PersistsCaseAndRunsVerifier()
    {
        var orch = CreateOrchestrator(out var store, out _);
        var first = await orch.HandleAsync(new AgentMessageRequest(
            "帮我把明天去杭州的酒店免费取消。",
            ScenarioId: "A"));

        Assert.True(first.HasPendingApprovals);
        var pending = first.PendingApprovals![0];

        var approved = await orch.RespondToApprovalAsync(new FunctionApprovalRequest(
            first.AgentSessionId!, pending.RequestId, true, "user confirmed"));

        Assert.Equal("function_approval", approved.Intent);
        Assert.Equal("WriteApproved", approved.Action);
        Assert.True(approved.VerificationPassed, string.Join("; ", approved.VerificationViolations));
        Assert.Contains(store.Cases, c => c.CaseId == first.CaseId);
        Assert.Contains(store.WorkflowRuns, r => r.CaseId == first.CaseId && r.RunId == approved.RunId);
    }

    [Fact]
    public async Task HandleStreamAsync_EmitsPhasedStatusAndDone()
    {
        var orch = CreateOrchestrator(out _, out _);
        var events = new List<AgentStreamEvent>();
        await foreach (var ev in orch.HandleStreamAsync(new AgentMessageRequest(
                           "帮我查一下退款进度，现在钱没到账。", ScenarioId: "C")))
        {
            events.Add(ev);
        }

        Assert.Contains(events, e => e.Type == "status" && e.Text == "assembling_context");
        Assert.Contains(events, e => e.Type == "status" && e.Text == "agent_running");
        Assert.Contains(events, e => e.Type == "status" && e.Text == "agent_completed");
        Assert.Contains(events, e => e.Type == "step");
        Assert.Contains(events, e => e.Type == "tool");
        Assert.Contains(events, e => e.Type == "reply_delta");
        Assert.Contains(events, e => e.Type == "done");
        // Real stream: tool/reply events appear before done, not as post-hoc replay only.
        var doneIdx = events.FindIndex(e => e.Type == "done");
        var toolIdx = events.FindIndex(e => e.Type == "tool");
        Assert.True(toolIdx >= 0 && toolIdx < doneIdx);
    }

    [Fact]
    public async Task ConfirmWrite_AutoApprovesViaFunctionApprovalPath()
    {
        var orch = CreateOrchestrator(out _, out _);
        var decision = await orch.HandleAsync(new AgentMessageRequest(
            "帮我把明天去杭州的酒店免费取消。",
            ScenarioId: "A",
            ConfirmWrite: true));

        Assert.True(decision.AgentDriven);
        Assert.False(decision.HasPendingApprovals);
        Assert.Equal("function_approval", decision.Intent);
        Assert.Contains("submit_cancellation", decision.ToolSequence);
        Assert.DoesNotContain(decision.Steps, s => s.Step == "确认写操作");
    }

    [Fact]
    public async Task HitlGate_AlwaysMentionsFunctionApproval()
    {
        var orch = CreateOrchestrator(out _, out _);
        var decision = await orch.HandleAsync(new AgentMessageRequest(
            "帮我把明天去杭州的酒店免费取消。",
            ScenarioId: "A"));

        Assert.True(decision.HasPendingApprovals);
        Assert.NotNull(decision.Hitl);
        Assert.Contains("FunctionApproval", decision.Hitl!.Gate);
        Assert.DoesNotContain(decision.Steps, s => s.Step == "确认写操作");
    }
}

public class PluginModelTests
{
    [Fact]
    public void Registry_ListsRefundAndEcho()
    {
        var refund = new RefundAgentPlugin();
        var echo = new StayOta.Agent.Plugins.Echo.EchoAgentPlugin();
        var registry = new AgentPluginRegistry([refund, echo]);
        Assert.Equal("refund", registry.Primary.Id);
        Assert.Equal(2, registry.Plugins.Count);
        Assert.NotNull(registry.Get("echo"));
    }

    [Fact]
    public async Task CompositeCatalog_IncludesEchoAndRefundTools()
    {
        TestPaths.EnsureRootEnv();
        var store = new MemoryRefundDataStore();
        var confirm = new MemoryConfirmationStore();
        var idem = new MemoryIdempotencyStore();
        var policy = new CompositeToolPolicy(
        [
            new RefundToolPolicyContribution(),
            new StayOta.Agent.Plugins.Echo.EchoAgentPlugin().ToolPolicy
        ]);
        var gateway = new ToolGateway(store, confirm, idem, policy, NullLogger<ToolGateway>.Instance);
        var refundCatalog = new RefundAiToolCatalog(gateway, store, policy, new RefundToolFailureReplanner());
        var echoCatalog = new StayOta.Agent.Plugins.Echo.EchoAiToolCatalog();
        var composite = new CompositeAgentToolCatalog([refundCatalog, echoCatalog]);

        Assert.Contains("get_order_detail", composite.Functions.Keys);
        Assert.Contains("echo_ping", composite.Functions.Keys);
        Assert.Contains("echo_reflect", composite.Functions.Keys);

        var ping = await echoCatalog.InvokeAsync(new ToolCall(
            "trc_echo", "echo_ping", ToolAccess.Read, "u", null, null, RiskLevel.L1, "INTENT_READY",
            new Dictionary<string, object?>()), default);
        Assert.True(ping.Allowed && ping.Success);

        Assert.Equal("INTENT_READY", policy.StateFor("echo_ping"));
        Assert.False(policy.IsWrite("echo_ping"));
        Assert.False(policy.RequiresConfirmation("echo_reflect"));
    }
}
