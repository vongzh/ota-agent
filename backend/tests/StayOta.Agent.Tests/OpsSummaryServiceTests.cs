using System.Collections.Concurrent;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Host.Services;
using StayOta.Agent.Plugins;
using StayOta.Agent.Plugins.Echo;
using StayOta.Agent.Plugins.Refund;
using Xunit;

namespace StayOta.Agent.Tests;

public class OpsSummaryServiceTests
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

        public Task<IReadOnlyList<AgentSessionSummary>> ListAsync(int take = 50, CancellationToken ct = default)
        {
            var list = _map
                .OrderByDescending(kv => kv.Value.UpdatedAt)
                .Take(take)
                .Select(kv => new AgentSessionSummary(
                    kv.Key, kv.Value.TraceId, kv.Value.UserId, kv.Value.OrderId, kv.Value.CaseId,
                    kv.Value.ScenarioId, kv.Value.PendingApprovals.Count, kv.Value.UpdatedAt))
                .ToList();
            return Task.FromResult<IReadOnlyList<AgentSessionSummary>>(list);
        }

        public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_map.TryRemove(sessionId, out _));
    }

    private sealed class StubEvalRunner : IEvalRunner
    {
        private readonly IReadOnlyList<EvalCaseDto> _cases;

        public StubEvalRunner(int count)
        {
            _cases = Enumerable.Range(1, count)
                .Select(i => new EvalCaseDto($"e{i}", "msg", "G", "L1"))
                .ToList();
        }

        public Task<IReadOnlyList<EvalResultDto>> RunAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EvalResultDto>>([]);

        public IReadOnlyList<EvalCaseDto> ListCases() => _cases;
    }

    [Fact]
    public async Task BuildAsync_AggregatesSessionsAuditsWorkflowsAndMarksBusinessPlaceholder()
    {
        var sessions = new MemoryAgentSessionStore();
        await sessions.SaveAsync("s1", new AgentSessionSnapshot
        {
            TraceId = "t1",
            UserId = "u1",
            ScenarioId = "G",
            PendingApprovals = [new PendingApprovalRecord { RequestId = "r1", ToolName = "submit_cancellation" }]
        });
        await sessions.SaveAsync("s2", new AgentSessionSnapshot
        {
            TraceId = "t2",
            UserId = "u2",
            ScenarioId = "A"
        });

        var store = new MemoryRefundDataStore();
        await store.SaveToolAuditAsync(new ToolAuditLog
        {
            TraceId = "t1",
            ToolName = "get_order_detail",
            Access = ToolAccess.Read,
            Allowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.SaveToolAuditAsync(new ToolAuditLog
        {
            TraceId = "t1",
            ToolName = "submit_cancellation",
            Access = ToolAccess.Write,
            Allowed = false,
            DenyReason = "write requires approval",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.SaveWorkflowRunAsync(new WorkflowRun
        {
            RunId = "run1",
            CaseId = "c1",
            ScenarioId = "G",
            Status = "SUCCEEDED",
            StartedAt = DateTimeOffset.UtcNow
        });
        await store.SaveWorkflowRunAsync(new WorkflowRun
        {
            RunId = "run2",
            CaseId = "c2",
            ScenarioId = "A",
            Status = "ASSERTION_FAILED",
            StartedAt = DateTimeOffset.UtcNow
        });
        await store.UpsertCaseAsync(new RefundCase
        {
            CaseId = "c1",
            OrderId = "o1",
            UserId = "u1",
            ScenarioId = "G",
            Status = "REFUND_INITIATED",
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var registry = new AgentPluginRegistry([new RefundAgentPlugin(), new EchoAgentPlugin()]);
        var svc = new OpsSummaryService(sessions, store, new StubEvalRunner(36), registry);

        var summary = await svc.BuildAsync();

        Assert.Equal("agent-process", summary.Source);
        Assert.True(summary.NorthStar.IsProcessMetric);
        Assert.Equal("pending", summary.BusinessNorthStar.Status);
        Assert.Contains("业务侧待接入", summary.BusinessNorthStar.Note);

        Assert.Equal(2, summary.Sessions.Total);
        Assert.Equal(1, summary.Sessions.WithPendingApproval);
        Assert.Equal(1, summary.Sessions.PendingApprovalTotal);

        Assert.Equal(2, summary.Audits.Total);
        Assert.Equal(1, summary.Audits.Allowed);
        Assert.Equal(1, summary.Audits.Denied);
        Assert.Equal(0.5, summary.Audits.DenyRate);

        Assert.Equal(2, summary.Workflows.Total);
        Assert.Equal(1, summary.Workflows.Succeeded);
        Assert.Equal(1, summary.Workflows.Failed);

        Assert.Equal(1, summary.Cases.Total);
        Assert.Equal(36, summary.Eval.CaseCount);
        Assert.Equal(2, summary.Plugins.Count);

        Assert.Contains(summary.Metrics, m => m.Name == "活跃 Session" && m.Value == "2");
        Assert.Contains(summary.Funnel, f => f.Stage == "Tool 审计" && f.In == 2);
        Assert.Contains(summary.RiskItems, r => r.Type.Contains("approval") && r.Count == 1);
        Assert.Equal("50%", summary.NorthStar.Value);
    }

    [Fact]
    public async Task BuildAsync_EmptyStores_ReturnsZerosWithoutFakePercents()
    {
        var store = new MemoryRefundDataStore();
        var registry = new AgentPluginRegistry([new RefundAgentPlugin()]);
        var svc = new OpsSummaryService(
            new MemoryAgentSessionStore(), store, new StubEvalRunner(36), registry);

        var summary = await svc.BuildAsync();

        Assert.Equal(0, summary.Sessions.Total);
        Assert.Equal(0, summary.Audits.Total);
        Assert.Equal("—", summary.NorthStar.Value);
        Assert.Contains(summary.Metrics, m => m.Name == "Tool 拦截率" && m.Value == "—");
        Assert.Contains(summary.RiskItems, r => r.Type == "暂无拦截" && r.Count == 0);
    }
}
