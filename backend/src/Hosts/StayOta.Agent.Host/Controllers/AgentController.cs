using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Contracts;

namespace StayOta.Agent.Host.Controllers;

[ApiController]
[Route("api")]
public sealed class AgentController(
    IAgentOrchestrator orchestrator,
    IScenarioCatalog scenarios,
    IConfirmationStore confirmationStore,
    IToolGateway tools,
    IEvalRunner evalRunner,
    IScenarioWorkflow workflow,
    IOptions<HostingOptions> hostingOptions) : ControllerBase
{
    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ScenarioDto>> ListScenarios() => Ok(scenarios.List());

    [HttpGet("tools")]
    public ActionResult<IReadOnlyList<ToolContractDto>> ListTools() => Ok(tools.ListContracts());

    [HttpGet("hosting")]
    public ActionResult<object> Hosting()
    {
        var h = hostingOptions.Value;
        return Ok(new
        {
            demoEnabled = h.DemoEnabled,
            authRequired = !string.IsNullOrWhiteSpace(h.ApiKey),
            resetDatabaseOnStartup = h.ResetDatabaseOnStartup
        });
    }

    [HttpPost("agent/message")]
    public async Task<ActionResult<AgentDecisionDto>> Message([FromBody] AgentMessageRequest request, CancellationToken ct)
    {
        try
        {
            var result = await orchestrator.HandleAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>SSE stream: status → step/tool → reply_delta → done(decision).</summary>
    [HttpPost("agent/message/stream")]
    public async Task MessageStream([FromBody] AgentMessageRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var evt in orchestrator.HandleStreamAsync(request, ct))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(evt, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                await Response.WriteAsync($"event: {evt.Type}\n", ct);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (InvalidOperationException ex)
        {
            var err = System.Text.Json.JsonSerializer.Serialize(new { type = "error", text = ex.Message });
            await Response.WriteAsync("event: error\n", ct);
            await Response.WriteAsync($"data: {err}\n\n", ct);
        }
    }

    [HttpPost("agent/approvals")]
    public async Task<ActionResult<AgentDecisionDto>> RespondToApproval(
        [FromBody] FunctionApprovalRequest request, CancellationToken ct)
    {
        try
        {
            var result = await orchestrator.RespondToApprovalAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("workflows/{scenarioId}/run")]
    public async Task<ActionResult<WorkflowRunResultDto>> RunWorkflow(string scenarioId, CancellationToken ct)
    {
        if (!RequireDemo()) return DemoOnly();
        try
        {
            var result = await workflow.RunAsync(scenarioId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("workflows/run-all")]
    public async Task<ActionResult<object>> RunAllWorkflows(CancellationToken ct)
    {
        if (!RequireDemo()) return DemoOnly();
        var results = new List<WorkflowRunResultDto>();
        foreach (var id in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" })
        {
            results.Add(await workflow.RunAsync(id, ct));
        }
        return Ok(new
        {
            total = results.Count,
            succeeded = results.Count(r => r.Succeeded),
            failed = results.Count(r => !r.Succeeded),
            results
        });
    }

    [HttpPost("confirmations")]
    public async Task<ActionResult<ConfirmActionResponse>> Confirm([FromBody] ConfirmActionRequest request, CancellationToken ct)
    {
        if (!RequireDemo()) return DemoOnly();
        var token = await confirmationStore.IssueAsync(
            request.CaseId, request.OrderId, request.OrderVersion, request.Action, TimeSpan.FromMinutes(10), ct);
        return Ok(new ConfirmActionResponse(true, "confirmation issued", token));
    }

    [HttpGet("eval/cases")]
    public ActionResult<IReadOnlyList<EvalCaseDto>> EvalCases()
    {
        if (!RequireDemo()) return DemoOnly();
        return Ok(evalRunner.ListCases());
    }

    [HttpPost("eval/run")]
    public async Task<ActionResult<object>> RunEval(CancellationToken ct)
    {
        if (!RequireDemo()) return DemoOnly();
        var results = await evalRunner.RunAllAsync(ct);
        return Ok(new
        {
            total = results.Count,
            passed = results.Count(r => r.Passed),
            failed = results.Count(r => !r.Passed),
            results
        });
    }

    private bool RequireDemo() => hostingOptions.Value.DemoEnabled;

    private ObjectResult DemoOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "endpoint is demo-only; set Hosting:DemoEnabled=true" });
}
