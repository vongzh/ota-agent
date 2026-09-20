using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Plugins.Refund.Services;
using Xunit;

namespace StayOta.Agent.Tests;

public class ScenarioWorkflowTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("I")]
    public async Task CoreScenarios_Succeed(string scenarioId)
    {
        TestPaths.EnsureRootEnv();
        var workflow = GatewayFactory.CreateWorkflow();
        var result = await workflow.RunAsync(scenarioId);
        Assert.True(result.Succeeded, $"scenario {scenarioId} failed: tools={string.Join(',', result.ToolCalls)}");
        Assert.NotEmpty(result.ToolCalls);
        Assert.NotEmpty(result.Steps);
    }

    [Fact]
    public async Task RunAll_AtoL_AllSucceed()
    {
        TestPaths.EnsureRootEnv();
        var store = new MemoryRefundDataStore();
        var workflow = GatewayFactory.CreateWorkflow(store);
        var failed = new List<string>();
        foreach (var id in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" })
        {
            var result = await workflow.RunAsync(id);
            if (!result.Succeeded)
                failed.Add($"{id}:{result.CaseStatus}:{string.Join('|', result.ToolCalls)}");
        }

        Assert.True(failed.Count == 0, "failed scenarios: " + string.Join("; ", failed));
        Assert.Equal(12, store.WorkflowRuns.Count);
    }
}

public class EvalRunnerTests
{
    private sealed class FakeOrchestrator : IAgentOrchestrator
    {
        public Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default)
        {
            var scenario = request.ScenarioId ?? "A";
            var action = scenario switch
            {
                "C" => "ExplainProgress",
                "E" or "K" or "L" => "HumanHandoff",
                "D" => "Recovery",
                "I" => "ChangeOrder",
                "J" => "FinanceReview",
                "G" => "RequestEvidence",
                "H" => "ServiceDispute",
                "F" => "RequestInformation",
                _ => "ConfirmCancel"
            };
            var tools = scenario switch
            {
                "C" => new List<string> { "list_user_orders", "get_order_detail", "get_refund_status", "get_payment_events" },
                "E" => new List<string> { "list_user_orders", "get_order_detail", "create_human_handoff" },
                _ => new List<string> { "list_user_orders", "get_order_detail", "get_policy_snapshot", "calculate_refund_quote" }
            };
            var reply = scenario == "C"
                ? "退款仍在渠道处理中，请继续等待。"
                : "确认后可取消，费用以报价为准。";
            var refund = scenario is "A" or "B" or "C" ? 100m : (decimal?)null;
            var fee = scenario == "A" ? 0m : scenario == "B" ? 50m : 0m;
            var order = new HotelOrderDto(
                $"ORD-{scenario}-001", "demo", DateOnly.FromDateTime(DateTime.Today),
                DateOnly.FromDateTime(DateTime.Today.AddDays(1)), 100, "CNY", "CONFIRMED", false,
                "POL", 1, "room", 1);
            var dto = new AgentDecisionDto(
                "t", "r", $"CASE-{scenario}-001", scenario, "intent", 0.9,
                StayOta.Agent.Abstractions.Domain.RiskLevel.L1, 10, action,
                "c", "p", "copy", refund, fee, reply, "DECISION_READY", "OPEN",
                [
                    new("意图识别", "success", "a"),
                    new("槽位提取", "success", "b"),
                    new("订单查询", "success", "c"),
                    new("政策检索", "success", "d"),
                    new("规则校验", "success", "e"),
                    new("风险判断", "success", "f"),
                    new("处理动作", "active", "g")
                ],
                new Dictionary<string, string>(),
                [new("P", "t", 0.9, "s")],
                tools, null, order, true, []);
            return Task.FromResult(dto);
        }

        public Task<AgentDecisionDto> RespondToApprovalAsync(FunctionApprovalRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<AgentStreamEvent> HandleStreamAsync(
            AgentMessageRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var decision = await HandleAsync(request, ct);
            yield return new AgentStreamEvent("done", null, decision);
        }
    }

    [Fact]
    public void ListCases_Loads36FromRepo()
    {
        TestPaths.EnsureRootEnv();
        var runner = new EvalRunner(new MemoryRefundDataStore(), new FakeOrchestrator());
        Assert.Equal(36, runner.ListCases().Count);
    }

    [Fact]
    public async Task RunAll_PassesWhenRouterAndOrchestratorAlign()
    {
        TestPaths.EnsureRootEnv();
        var runner = new EvalRunner(new MemoryRefundDataStore(), new FakeOrchestrator());
        var results = await runner.RunAllAsync();
        Assert.Equal(36, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"{r.Id} routed={r.ActualScenario} detail={r.Detail}"));
    }
}
