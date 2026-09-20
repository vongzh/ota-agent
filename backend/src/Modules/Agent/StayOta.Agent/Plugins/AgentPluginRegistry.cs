using StayOta.Agent.Abstractions.Plugins;

namespace StayOta.Agent.Plugins;

public sealed class AgentPluginRegistry(IEnumerable<IAgentPlugin> plugins) : IAgentPluginRegistry
{
    private readonly IReadOnlyList<IAgentPlugin> _plugins = plugins.ToList();

    public IReadOnlyList<IAgentPlugin> Plugins => _plugins;

    public IAgentPlugin Primary =>
        _plugins.FirstOrDefault()
        ?? throw new InvalidOperationException("No IAgentPlugin registered. Call AddAgentPlugin<T>().");

    public IAgentPlugin? Get(string id) =>
        _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
