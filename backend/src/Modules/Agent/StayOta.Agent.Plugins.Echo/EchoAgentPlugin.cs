using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins.Echo;

/// <summary>
/// Minimal second vertical — proves <see cref="IAgentPlugin"/> registration without Refund domain.
/// Does not replace the primary refund orchestrator; contributes policy for a stub tool name
/// and registers a marker service discoverable via DI / health.
/// </summary>
public sealed class EchoAgentPlugin : IAgentPlugin
{
    public string Id => "echo";
    public string DisplayName => "Echo (sample vertical)";
    public IToolPolicyContribution ToolPolicy { get; } = CreatePolicy();

    public string AgentInstructions =>
        "You are a sample Echo agent. Prefer the echo_ping tool for connectivity checks.";

    public string AgentName => "stayota-echo-agent";
    public string AgentDescription => "Minimal sample plugin for framework authors";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IEchoPluginMarker, EchoPluginMarker>();
    }

    private static ToolPolicyContribution CreatePolicy()
    {
        var c = new ToolPolicyContribution();
        c.DefaultStates["echo_ping"] = "INTENT_READY";
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
