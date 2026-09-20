using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Echo;

/// <summary>Minimal Echo vertical intent picks for DeterministicChatClient.</summary>
file sealed class EchoDeterministicIntentPlanner : IDeterministicIntentPlanner
{
    public string PluginId => "echo";

    public IReadOnlyList<string> Select(
        string message,
        string conversationState,
        IReadOnlyCollection<string> availableTools,
        string? preferredWriteTool = null)
    {
        var available = new HashSet<string>(availableTools, StringComparer.Ordinal);
        var picks = new List<string>();
        void Add(string name)
        {
            if (available.Contains(name) && !picks.Contains(name, StringComparer.Ordinal))
                picks.Add(name);
        }

        var msg = message ?? "";
        if (ContainsAny(msg, "echo", "ping", "连通", "探活", "reflect", "回声"))
        {
            Add("echo_ping");
            if (ContainsAny(msg, "reflect", "回声", "复述"))
                Add("echo_reflect");
        }

        if (!string.IsNullOrWhiteSpace(preferredWriteTool))
            Add(preferredWriteTool!);

        return picks.Take(4).ToList();
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Runnable second vertical — tools + policy + MCP, coexists with Refund via composite catalogs.
/// </summary>
public sealed class EchoAgentPlugin : IAgentPlugin
{
    public string Id => "echo";
    public string DisplayName => "Echo (sample vertical)";
    public IToolPolicyContribution ToolPolicy { get; } = CreatePolicy();

    public string AgentInstructions =>
        "You are a sample Echo agent. Prefer the echo_ping and echo_reflect tools for connectivity checks.";

    public string AgentName => "stayota-echo-agent";
    public string AgentDescription => "Minimal runnable sample plugin for framework authors";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IEchoPluginMarker, EchoPluginMarker>();
        services.AddSingleton<IDeterministicIntentPlanner, EchoDeterministicIntentPlanner>();
        services.AddSingleton<EchoAiToolCatalog>();
        services.AddSingleton<IPluginToolCatalog>(sp => sp.GetRequiredService<EchoAiToolCatalog>());
        // Additive MCP tools (Refund already called AddMcpServer + WithHttpTransport).
        services.AddMcpServer().WithTools<EchoMcpTools>();
    }

    private static ToolPolicyContribution CreatePolicy()
    {
        var c = new ToolPolicyContribution();
        c.DefaultStates["echo_ping"] = "INTENT_READY";
        c.DefaultStates["echo_reflect"] = "INTENT_READY";
        return c;
    }
}

public interface IEchoPluginMarker
{
    string PluginId { get; }
}

file sealed class EchoPluginMarker : IEchoPluginMarker
{
    public string PluginId => "echo";
}

/// <summary>In-process Echo tools — no Postgres; proves a second catalog in the composite.</summary>
public sealed class EchoAiToolCatalog : IPluginToolCatalog
{
    private readonly Dictionary<string, AIFunction> _functions;

    public EchoAiToolCatalog()
    {
        _functions = new Dictionary<string, AIFunction>(StringComparer.Ordinal)
        {
            ["echo_ping"] = AIFunctionFactory.Create(
                (CancellationToken _) => Task.FromResult<object>(new
                {
                    ok = true,
                    plugin = "echo",
                    tool = "echo_ping",
                    at = DateTimeOffset.UtcNow
                }),
                "echo_ping",
                "Connectivity check for the Echo sample plugin"),
            ["echo_reflect"] = AIFunctionFactory.Create(
                ([Description("Text to echo back")] string message, CancellationToken _) =>
                    Task.FromResult<object>(new
                    {
                        plugin = "echo",
                        tool = "echo_reflect",
                        echo = message ?? ""
                    }),
                "echo_reflect",
                "Echo the provided message back")
        };
    }

    public string PluginId => "echo";
    public IReadOnlyDictionary<string, AIFunction> Functions => _functions;

    public bool OwnsTool(string toolName) => _functions.ContainsKey(toolName);

    public IReadOnlyList<AITool> GetAiTools(bool requireApprovalForWrites = true) =>
        _functions.Values.Cast<AITool>().ToList();

    public async Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default)
    {
        if (!_functions.TryGetValue(call.ToolName, out var fn))
            return new ToolResult(false, false, call.ToolName, null, $"unknown echo tool: {call.ToolName}");

        var args = new AIFunctionArguments(
            call.Arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        var data = await fn.InvokeAsync(args, ct);
        return new ToolResult(true, true, call.ToolName, data, null);
    }
}

[McpServerToolType]
public sealed class EchoMcpTools
{
    [McpServerTool(Name = "echo_ping"), Description("Echo plugin connectivity check")]
    public string Ping() =>
        JsonSerializer.Serialize(new { ok = true, plugin = "echo", at = DateTimeOffset.UtcNow });

    [McpServerTool(Name = "echo_reflect"), Description("Echo a message back")]
    public string Reflect([Description("Text to echo")] string message) =>
        JsonSerializer.Serialize(new { plugin = "echo", echo = message });
}
