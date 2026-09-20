using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Plugins.Refund.Services;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Plugins.Refund.Persistence;

namespace StayOta.Agent.Plugins.Refund.Persistence;

public sealed class RefundDataStore(AppDbContext db) : IRefundDataStore
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static List<ToolContractDto> _contracts = [];

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        LoadContracts();
        if (await db.Scenarios.AnyAsync(ct)) return;

        await SeedLock.WaitAsync(ct);
        try
        {
            if (await db.Scenarios.AnyAsync(ct)) return;
            SeedSync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public async Task ResetDemoAsync(CancellationToken ct = default)
    {
        await SeedLock.WaitAsync(ct);
        try
        {
            db.ToolAudits.RemoveRange(db.ToolAudits);
            db.CaseEvents.RemoveRange(db.CaseEvents);
            db.WorkflowRuns.RemoveRange(db.WorkflowRuns);
            db.Cases.RemoveRange(db.Cases);
            db.Orders.RemoveRange(db.Orders);
            db.Policies.RemoveRange(db.Policies);
            db.Scenarios.RemoveRange(db.Scenarios);
            await db.SaveChangesAsync(ct);
            SeedSync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public ScenarioFixture GetScenario(string scenarioId) =>
        db.Scenarios.AsNoTracking().First(x => x.ScenarioId == scenarioId.ToUpperInvariant());

    public IReadOnlyList<ScenarioFixture> ListScenarios() =>
        db.Scenarios.AsNoTracking().OrderBy(x => x.ScenarioId).ToList();

    public Task<HotelOrder?> GetOrderAsync(string orderId, CancellationToken ct = default) =>
        db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, ct);

    public async Task<IReadOnlyList<HotelOrder>> ListOrdersAsync(string userId, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);

    public Task<PolicySnapshot?> GetPolicyAsync(string policyId, CancellationToken ct = default) =>
        db.Policies.AsNoTracking().FirstOrDefaultAsync(x => x.PolicyId == policyId, ct);

    public async Task<RefundCase> UpsertCaseAsync(RefundCase refundCase, CancellationToken ct = default)
    {
        var existing = await db.Cases.FirstOrDefaultAsync(x => x.CaseId == refundCase.CaseId, ct);
        if (existing is null) db.Cases.Add(refundCase);
        else
        {
            existing.Status = refundCase.Status;
            existing.RiskLevel = refundCase.RiskLevel;
            existing.Intent = refundCase.Intent;
            existing.RecommendedAction = refundCase.RecommendedAction;
            existing.QuoteRefundAmount = refundCase.QuoteRefundAmount;
            existing.QuoteFeeAmount = refundCase.QuoteFeeAmount;
            existing.ConversationState = refundCase.ConversationState;
            existing.UpdatedAt = refundCase.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
        return refundCase;
    }

    public async Task AppendEventAsync(string caseId, string eventType, object payload, CancellationToken ct = default)
    {
        db.CaseEvents.Add(new CaseEvent
        {
            CaseId = caseId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveWorkflowRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveToolAuditAsync(ToolAuditLog log, CancellationToken ct = default)
    {
        db.ToolAudits.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public IReadOnlyList<ToolContractDto> GetToolContracts()
    {
        LoadContracts();
        return _contracts;
    }

    private static void LoadContracts()
    {
        if (_contracts.Count > 0) return;
        var path = ResolvePath("contracts/tool-contracts.json");
        if (!File.Exists(path))
        {
            _contracts =
            [
                new ToolContractDto("get_order_detail", "READ", "order", ["ORDER_CONFIRMED", "FACTS_REQUIRED", "DECISION_READY", "TRACKING_REFUND"]),
                new ToolContractDto("get_policy_snapshot", "READ", "policy", ["ORDER_CONFIRMED", "FACTS_REQUIRED", "DECISION_READY"]),
                new ToolContractDto("calculate_refund_quote", "READ", "quote", ["DECISION_READY"]),
                new ToolContractDto("submit_cancellation", "WRITE", "cancel", ["CONFIRMATION_REQUIRED"]),
                new ToolContractDto("create_human_handoff", "WRITE", "handoff", ["FACTS_REQUIRED", "DECISION_READY", "OPTION_PRESENTED", "WAITING_EXTERNAL", "TRACKING_REFUND"])
            ];
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        _contracts = doc.RootElement.GetProperty("tools").EnumerateArray()
            .Select(t => new ToolContractDto(
                t.GetProperty("name").GetString()!,
                t.GetProperty("mode").GetString()!,
                t.GetProperty("purpose").GetString()!,
                t.TryGetProperty("allowed_conversation_states", out var states)
                    ? states.EnumerateArray().Select(x => x.GetString()!).ToList()
                    : []))
            .ToList();
    }

    private static string ResolvePath(string relative)
    {
        var root = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
        if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(root, relative);
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relative)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../", relative)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../", relative)),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private void SeedSync()
    {
        var hotels = new Dictionary<string, string>
        {
            ["A"] = "杭州湖畔演示酒店", ["B"] = "上海外滩演示酒店", ["C"] = "苏州园林演示酒店",
            ["D"] = "南京夫子庙演示酒店", ["E"] = "广州塔景演示酒店", ["F"] = "厦门海景演示酒店",
            ["G"] = "南城悦居酒店（演示）", ["H"] = "深圳湾演示酒店", ["I"] = "成都宽窄演示酒店",
            ["J"] = "武汉会展演示酒店", ["K"] = "东京湾演示酒店", ["L"] = "北京国贸团体酒店"
        };

        var fixturePath = ResolvePath("mock/scenario-fixtures.json");
        using var fixtures = JsonDocument.Parse(File.ReadAllText(fixturePath));
        foreach (var s in fixtures.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var id = s.GetProperty("scenario_id").GetString()!;
            var risk = Enum.Parse<RiskLevel>(s.GetProperty("risk_level").GetString()!);
            var tools = s.GetProperty("required_tools").EnumerateArray().Select(x => x.GetString()!).ToList();
            var states = s.GetProperty("expected_conversation_states").EnumerateArray().Select(x => x.GetString()!).ToList();
            var asserts = s.GetProperty("success_assertions").EnumerateArray().Select(x => x.GetString()!).ToList();
            db.Scenarios.Add(new ScenarioFixture
            {
                ScenarioId = id,
                Title = s.GetProperty("title").GetString()!,
                EntryMessage = s.GetProperty("entry_message").GetString()!,
                UserId = s.GetProperty("user_id").GetString()!,
                OrderId = s.GetProperty("order_id").GetString()!,
                CaseId = s.GetProperty("case_id").GetString()!,
                RiskLevel = risk,
                ExpectedRoute = s.GetProperty("expected_route").GetString()!,
                ExpectedCaseStatus = s.GetProperty("expected_case_status").GetString()!,
                RequiredToolsJson = JsonSerializer.Serialize(tools),
                ExpectedStatesJson = JsonSerializer.Serialize(states),
                SuccessAssertionsJson = JsonSerializer.Serialize(asserts),
                Group = id switch
                {
                    "A" or "B" or "F" or "I" => "取消与变更",
                    "C" or "J" => "退款与支付",
                    "D" or "E" or "H" => "履约与住宿",
                    _ => "特殊审核"
                },
                Goal = s.GetProperty("expected_route").GetString()!
            });
        }

        var orderPath = ResolvePath("mock/orders.json");
        using var orders = JsonDocument.Parse(File.ReadAllText(orderPath));
        foreach (var o in orders.RootElement.GetProperty("orders").EnumerateArray())
        {
            var sid = o.GetProperty("scenario_id").GetString()!;
            var amount = o.GetProperty("amount");
            db.Orders.Add(new HotelOrder
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
                UserOnSite = o.TryGetProperty("fulfillment_alert", out var fa) && fa.TryGetProperty("user_on_site", out var uos) && uos.GetBoolean(),
                ChannelType = o.GetProperty("channel_type").GetString()!,
                ExtraJson = o.ToString()
            });

            db.Policies.Add(new PolicySnapshot
            {
                PolicyId = o.GetProperty("policy_id").GetString()!,
                ScenarioId = sid,
                Title = sid switch
                {
                    "A" => "入住前免费取消",
                    "B" => "阶梯扣费取消",
                    "F" => "不可取消例外协商",
                    "G" => "航班取消例外",
                    _ => $"场景{sid}政策快照"
                },
                Summary = "成交时政策快照（Mock）",
                RuleCode = o.GetProperty("policy_id").GetString()!,
                FreeCancel = sid == "A",
                FixedFee = sid == "B" ? 600 : null,
                Authority = sid is "K" ? "supplier" : "platform",
                Tags = sid
            });
        }

        db.SaveChanges();
    }
}
