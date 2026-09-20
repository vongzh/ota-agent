using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Plugins;

public static class AgentPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers a vertical plugin: contributes <see cref="IToolPolicyContribution"/> and calls
    /// <see cref="IAgentPlugin.ConfigureServices"/>.
    /// </summary>
    public static IServiceCollection AddAgentPlugin<TPlugin>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TPlugin : class, IAgentPlugin, new()
    {
        var plugin = new TPlugin();
        services.AddSingleton<IAgentPlugin>(plugin);
        services.AddSingleton<IToolPolicyContribution>(plugin.ToolPolicy);
        plugin.ConfigureServices(services, configuration);
        return services;
    }
}
