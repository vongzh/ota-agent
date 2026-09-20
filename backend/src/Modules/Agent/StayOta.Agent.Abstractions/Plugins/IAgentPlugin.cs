using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Abstractions.Plugins;

/// <summary>
/// Declarative vertical plugin: contributes tool policy and registers DI (catalog, orchestrator, MCP, eval, …).
/// </summary>
public interface IAgentPlugin
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Write / confirm / conversation-state contribution for this vertical's tools.</summary>
    IToolPolicyContribution ToolPolicy { get; }

    /// <summary>System instructions for <see cref="Ai.IAgentHost"/> when this plugin is primary.</summary>
    string AgentInstructions { get; }

    string AgentName { get; }

    string AgentDescription { get; }

    /// <summary>Register plugin services (gateway, catalog, orchestrator, stores, MCP, …).</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}

/// <summary>Registered plugins in load order.</summary>
public interface IAgentPluginRegistry
{
    IReadOnlyList<IAgentPlugin> Plugins { get; }
    IAgentPlugin Primary { get; }
    IAgentPlugin? Get(string id);
}
