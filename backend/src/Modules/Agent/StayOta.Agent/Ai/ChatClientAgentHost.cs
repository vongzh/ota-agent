using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Plugins;

namespace StayOta.Agent.Ai;

/// <summary>
/// Vertical-agnostic ChatClientAgent host. Instructions come from the primary <see cref="IAgentPlugin"/>.
/// Agent is built lazily so <see cref="TurnHitlOptions"/> can control ApprovalRequired wrapping per turn.
/// </summary>
public sealed class ChatClientAgentHost : IAgentHost
{
    private readonly IChatClient _chatClient;
    private readonly IAgentToolCatalog _tools;
    private readonly IAgentPluginRegistry _plugins;
    private readonly TurnHitlOptions _hitl;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private AIAgent? _agent;
    private bool? _builtWithApproval;

    public ChatClientAgentHost(
        IChatClient chatClient,
        IAgentToolCatalog tools,
        IAgentPluginRegistry plugins,
        TurnHitlOptions hitl,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _chatClient = chatClient;
        _tools = tools;
        _plugins = plugins;
        _hitl = hitl;
        _loggerFactory = loggerFactory;
        _services = services;
        ChatClient = chatClient;
        ProviderName = chatClient.GetService<ChatClientMetadata>()?.ProviderName
                       ?? chatClient.GetType().Name;
    }

    public AIAgent Agent
    {
        get
        {
            var wantApproval = _hitl.RequireFunctionApproval;
            lock (_gate)
            {
                if (_agent is null || _builtWithApproval != wantApproval)
                {
                    _agent = Build(wantApproval);
                    _builtWithApproval = wantApproval;
                }

                return _agent;
            }
        }
    }

    public IChatClient ChatClient { get; }
    public string ProviderName { get; }

    private AIAgent Build(bool requireApprovalForWrites)
    {
        var primary = _plugins.Primary;
        var pipeline = _chatClient.AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .Build(_services);

        return new ChatClientAgent(
            pipeline,
            instructions: primary.AgentInstructions,
            name: primary.AgentName,
            description: primary.AgentDescription,
            tools: _tools.GetAiTools(requireApprovalForWrites: requireApprovalForWrites).ToList(),
            loggerFactory: _loggerFactory,
            services: _services);
    }
}
