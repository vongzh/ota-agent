using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Tools;
using StayOta.Agent.Plugins.Refund;
using StayOta.Agent.Plugins.Refund.Ai;
using StayOta.Agent.Plugins.Refund.Services;
using StayOta.Agent.Tools;

namespace StayOta.Agent.Tests;

internal static class TestPaths
{
    public static string AgentRoot
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return env;

            // tests/.../bin/Debug/net10.0 → repo root (6 levels up)
            var fromBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
            if (File.Exists(Path.Combine(fromBin, "contracts/tool-contracts.json")))
                return fromBin;

            var cwd = Directory.GetCurrentDirectory();
            foreach (var c in new[]
                     {
                         cwd,
                         Path.GetFullPath(Path.Combine(cwd, "..")),
                         Path.GetFullPath(Path.Combine(cwd, "../..")),
                         Path.GetFullPath(Path.Combine(cwd, "../../..")),
                     })
            {
                if (File.Exists(Path.Combine(c, "contracts/tool-contracts.json")))
                    return c;
            }

            return fromBin;
        }
    }

    public static void EnsureRootEnv() =>
        Environment.SetEnvironmentVariable("STAYOTA_AGENT_ROOT", AgentRoot);
}

internal sealed class MemoryConfirmationStore : IConfirmationStore
{
    private readonly ConcurrentDictionary<string, (string CaseId, string OrderId, int Version, string Action)> _tokens = new();

    public Task<string> IssueAsync(string caseId, string orderId, int version, string action, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = $"tok_{Guid.NewGuid():N}"[..20];
        _tokens[token] = (caseId, orderId, version, action);
        return Task.FromResult(token);
    }

    public Task<bool> ConsumeAsync(string token, string caseId, string orderId, int version, string action, CancellationToken ct = default)
    {
        if (!_tokens.TryRemove(token, out var entry))
            return Task.FromResult(false);
        var ok = entry.CaseId == caseId && entry.OrderId == orderId && entry.Version == version && entry.Action == action;
        return Task.FromResult(ok);
    }
}

internal sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, string> _keys = new();

    public Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult(_keys.TryAdd(key, "pending"));

    public Task CompleteAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default)
    {
        _keys[key] = responseJson;
        return Task.CompletedTask;
    }

    public Task<string?> TryGetCompletedAsync(string key, CancellationToken ct = default)
    {
        if (!_keys.TryGetValue(key, out var v) || v == "pending")
            return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(v);
    }

    public Task AbandonAsync(string key, CancellationToken ct = default)
    {
        if (_keys.TryGetValue(key, out var v) && v == "pending")
            _keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory seed from repo mock/contracts — no Postgres required.</summary>
internal sealed class MemoryRefundDataStore : IRefundDataStore
{
    private readonly List<ScenarioFixture> _scenarios = [];
    private readonly List<HotelOrder> _orders = [];
    private readonly List<PolicySnapshot> _policies = [];
    private readonly List<ToolContractDto> _contracts;
    public List<ToolAuditLog> Audits { get; } = [];
    public List<WorkflowRun> WorkflowRuns { get; } = [];
    public List<RefundCase> Cases { get; } = [];

    public MemoryRefundDataStore()
    {
        TestPaths.EnsureRootEnv();
        _contracts = LoadContracts();
        SeedFromFiles();
    }

    public Task EnsureSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetDemoAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ScenarioFixture GetScenario(string scenarioId) =>
        _scenarios.First(s => s.ScenarioId == scenarioId.ToUpperInvariant());

    public IReadOnlyList<ScenarioFixture> ListScenarios() => _scenarios.OrderBy(s => s.ScenarioId).ToList();

    public Task<HotelOrder?> GetOrderAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(_orders.FirstOrDefault(o => o.OrderId == orderId));

    public Task<IReadOnlyList<HotelOrder>> ListOrdersAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HotelOrder>>(_orders.Where(o => o.UserId == userId).ToList());

    public Task<PolicySnapshot?> GetPolicyAsync(string policyId, CancellationToken ct = default) =>
        Task.FromResult(_policies.FirstOrDefault(p => p.PolicyId == policyId));

    public Task<RefundCase> UpsertCaseAsync(RefundCase refundCase, CancellationToken ct = default)
    {
        var idx = Cases.FindIndex(c => c.CaseId == refundCase.CaseId);
        if (idx < 0) Cases.Add(refundCase);
        else Cases[idx] = refundCase;
        return Task.FromResult(refundCase);
    }

    public Task AppendEventAsync(string caseId, string eventType, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SaveWorkflowRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        WorkflowRuns.Add(run);
        return Task.CompletedTask;
    }

    public Task SaveToolAuditAsync(ToolAuditLog log, CancellationToken ct = default)
    {
        Audits.Add(log);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ToolContractDto> GetToolContracts() => _contracts;

    private static List<ToolContractDto> LoadContracts()
    {
        var path = Path.Combine(TestPaths.AgentRoot, "contracts/tool-contracts.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("tools").EnumerateArray()
            .Select(t => new ToolContractDto(
                t.GetProperty("name").GetString()!,
                t.GetProperty("mode").GetString()!,
                t.GetProperty("purpose").GetString()!,
                t.TryGetProperty("allowed_conversation_states", out var states)
                    ? states.EnumerateArray().Select(x => x.GetString()!).ToList()
                    : []))
            .ToList();
    }

    private void SeedFromFiles()
    {
        var hotels = new Dictionary<string, string>
        {
            ["A"] = "杭州湖畔演示酒店", ["B"] = "上海外滩演示酒店", ["C"] = "苏州园林演示酒店",
            ["D"] = "南京夫子庙演示酒店", ["E"] = "广州塔景演示酒店", ["F"] = "厦门海景演示酒店",
            ["G"] = "南城悦居酒店（演示）", ["H"] = "深圳湾演示酒店", ["I"] = "成都宽窄演示酒店",
            ["J"] = "武汉会展演示酒店", ["K"] = "东京湾演示酒店", ["L"] = "北京国贸团体酒店"
        };

        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.AgentRoot, "mock/scenario-fixtures.json")));
        foreach (var s in fixtures.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var id = s.GetProperty("scenario_id").GetString()!;
            var tools = s.GetProperty("required_tools").EnumerateArray().Select(x => x.GetString()!).ToList();
            _scenarios.Add(new ScenarioFixture
            {
                ScenarioId = id,
                Title = s.GetProperty("title").GetString()!,
                EntryMessage = s.GetProperty("entry_message").GetString()!,
                UserId = s.GetProperty("user_id").GetString()!,
                OrderId = s.GetProperty("order_id").GetString()!,
                CaseId = s.GetProperty("case_id").GetString()!,
                RiskLevel = Enum.Parse<RiskLevel>(s.GetProperty("risk_level").GetString()!),
                ExpectedRoute = s.GetProperty("expected_route").GetString()!,
                ExpectedCaseStatus = s.GetProperty("expected_case_status").GetString()!,
                RequiredToolsJson = JsonSerializer.Serialize(tools),
                ExpectedStatesJson = "[]",
                SuccessAssertionsJson = "[]",
                Group = "test",
                Goal = s.GetProperty("expected_route").GetString()!
            });
        }

        using var orders = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.AgentRoot, "mock/orders.json")));
        foreach (var o in orders.RootElement.GetProperty("orders").EnumerateArray())
        {
            var sid = o.GetProperty("scenario_id").GetString()!;
            var amount = o.GetProperty("amount");
            _orders.Add(new HotelOrder
            {
                OrderId = o.GetProperty("order_id").GetString()!,
                ScenarioId = sid,
                UserId = o.GetProperty("user_id").GetString()!,
                HotelName = hotels[sid],
                PolicyId = o.GetProperty("policy_id").GetString()!,
                PaymentId = o.GetProperty("payment_id").GetString()!,
                RefundId = o.TryGetProperty("refund_id", out var rid) ? rid.GetString() : null,
                Status = o.GetProperty("status").GetString()!,
                Version = o.GetProperty("version").GetInt32(),
                CheckIn = DateOnly.Parse(o.GetProperty("check_in").GetString()!),
                CheckOut = DateOnly.Parse(o.GetProperty("check_out").GetString()!),
                RoomType = o.GetProperty("room_type").GetString()!,
                RoomCount = o.GetProperty("room_count").GetInt32(),
                PaidAmount = amount.GetProperty("paid").GetDecimal(),
                Currency = amount.GetProperty("currency").GetString()!,
                UserOnSite = false,
                ChannelType = o.GetProperty("channel_type").GetString()!
            });
            _policies.Add(new PolicySnapshot
            {
                PolicyId = o.GetProperty("policy_id").GetString()!,
                ScenarioId = sid,
                Title = $"场景{sid}政策",
                Summary = "mock",
                RuleCode = o.GetProperty("policy_id").GetString()!
            });
        }
    }
}

internal static class GatewayFactory
{
    private static readonly IToolPolicy Policy =
        new CompositeToolPolicy([new RefundToolPolicyContribution()]);

    public static (ToolGateway Gateway, MemoryRefundDataStore Store, MemoryConfirmationStore Confirm) Create()
    {
        var store = new MemoryRefundDataStore();
        var confirm = new MemoryConfirmationStore();
        var idem = new MemoryIdempotencyStore();
        var gateway = new ToolGateway(store, confirm, idem, Policy, NullLogger<ToolGateway>.Instance);
        return (gateway, store, confirm);
    }

    public static ScenarioWorkflow CreateWorkflow(MemoryRefundDataStore? store = null)
    {
        store ??= new MemoryRefundDataStore();
        var confirm = new MemoryConfirmationStore();
        var idem = new MemoryIdempotencyStore();
        var gateway = new ToolGateway(store, confirm, idem, Policy, NullLogger<ToolGateway>.Instance);
        var catalog = new RefundAiToolCatalog(gateway, store, Policy);
        return new ScenarioWorkflow(store, catalog, confirm, Policy, new Verifier(), NullLogger<ScenarioWorkflow>.Instance);
    }
}
