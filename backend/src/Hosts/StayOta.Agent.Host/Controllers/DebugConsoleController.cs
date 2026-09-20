using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Ai;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Host.Controllers;

[ApiController]
[Route("api")]
public sealed class DebugConsoleController(
    IAgentPluginRegistry plugins,
    IAgentToolCatalog toolCatalog,
    IToolGateway tools,
    IChatClientFactory chatClientFactory,
    RuntimeAiOptions runtimeAi,
    IOptions<AiOptions> aiOptions,
    IOptions<HostingOptions> hostingOptions) : ControllerBase
{
    [HttpGet("plugins")]
    public ActionResult<object> ListPlugins() =>
        Ok(plugins.Plugins.Select(p => new
        {
            p.Id,
            p.DisplayName,
            p.AgentName,
            isPrimary = ReferenceEquals(p, plugins.Primary)
        }).ToList());

    [HttpGet("ai/provider")]
    public ActionResult<object> GetAiProvider()
    {
        var cfg = aiOptions.Value;
        var model = cfg.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            ? cfg.Ollama.Model
            : cfg.OpenAI.Model;
        return Ok(runtimeAi.Snapshot(cfg.Provider, model));
    }

    [HttpPost("ai/provider")]
    public ActionResult<object> SetAiProvider([FromBody] AiProviderRequest request)
    {
        if (!hostingOptions.Value.DemoEnabled)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "provider switch is demo-only" });

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Deterministic", "OpenAI", "Ollama" };
        if (string.IsNullOrWhiteSpace(request.Provider) || !allowed.Contains(request.Provider))
            return BadRequest(new { message = "provider must be Deterministic|OpenAI|Ollama" });

        runtimeAi.Set(request.Provider, request.Model);
        var cfg = aiOptions.Value;
        var model = cfg.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            ? cfg.Ollama.Model
            : cfg.OpenAI.Model;
        return Ok(new
        {
            ok = true,
            effectiveProvider = chatClientFactory.ProviderName,
            snapshot = runtimeAi.Snapshot(request.Provider, request.Model ?? model)
        });
    }

    /// <summary>Try-invoke a catalog tool (MCP Inspector / debug console).</summary>
    [HttpPost("tools/invoke")]
    public async Task<ActionResult<object>> InvokeTool([FromBody] ToolInvokeRequest request, CancellationToken ct)
    {
        if (!hostingOptions.Value.DemoEnabled)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "tool invoke is demo-only" });
        if (string.IsNullOrWhiteSpace(request.ToolName))
            return BadRequest(new { message = "toolName required" });

        // Prefer composite catalog (covers Echo + Refund); fall back to refund gateway list.
        if (toolCatalog.Functions.ContainsKey(request.ToolName) ||
            tools.ListContracts().Any(c => c.Name == request.ToolName))
        {
            var call = new ToolCall(
                request.TraceId ?? $"trc_dbg_{Guid.NewGuid():N}"[..16],
                request.ToolName,
                ToolAccess.Read,
                request.UserId ?? "debug-user",
                request.OrderId,
                request.CaseId,
                RiskLevel.L1,
                request.ConversationState ?? "INTENT_READY",
                request.Arguments ?? new Dictionary<string, object?>());

            ToolResult result;
            try
            {
                result = toolCatalog.Functions.ContainsKey(request.ToolName)
                    ? await toolCatalog.InvokeAsync(call, ct)
                    : await tools.InvokeAsync(call, ct);
            }
            catch (Exception ex)
            {
                return Ok(new { allowed = false, success = false, tool = request.ToolName, error = ex.Message });
            }

            return Ok(new
            {
                result.Allowed,
                result.Success,
                result.ToolName,
                result.DenyReason,
                result.Data
            });
        }

        return NotFound(new { message = $"unknown tool {request.ToolName}" });
    }

    [HttpGet("mcp/tools")]
    public ActionResult<object> ListMcpFacingTools()
    {
        // Host-facing inventory: refund contracts + echo AI functions.
        var refund = tools.ListContracts().Select(c => new
        {
            name = c.Name,
            source = "refund",
            access = c.Mode,
            purpose = c.Purpose
        });
        var echo = toolCatalog.Functions.Keys
            .Where(n => n.StartsWith("echo_", StringComparison.Ordinal))
            .Select(n => new { name = n, source = "echo", access = "READ", purpose = "Echo sample tool" });
        return Ok(refund.Concat(echo).ToList());
    }
}

public sealed record AiProviderRequest(string Provider, string? Model = null);

public sealed record ToolInvokeRequest(
    string ToolName,
    Dictionary<string, object?>? Arguments = null,
    string? UserId = null,
    string? OrderId = null,
    string? CaseId = null,
    string? ConversationState = null,
    string? TraceId = null);
