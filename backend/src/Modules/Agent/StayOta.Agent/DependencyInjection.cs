using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Abstractions.Security;
using StayOta.Agent.Abstractions.Tools;
using StayOta.Agent.Ai;
using StayOta.Agent.Plugins;
using StayOta.Agent.Redis;
using StayOta.Agent.Tools;

namespace StayOta.Agent;

public static class StayOtaAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared Agent runtime: options, Redis stores, chat client, conversation host, tool policy composite.
    /// Call <c>AddAgentPlugin&lt;T&gt;</c> afterwards for each vertical.
    /// </summary>
    public static IServiceCollection AddStayOtaAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var hosting = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
        var isProductionLike = !hosting.DemoEnabled;

        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            if (isProductionLike)
                throw new InvalidOperationException("ConnectionStrings:Redis is required when DemoEnabled=false");
            redis = "127.0.0.1:6379";
        }

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<ProductionOptions>(configuration.GetSection(ProductionOptions.SectionName));
        services.Configure<HostingOptions>(configuration.GetSection(HostingOptions.SectionName));
        services.Configure<AgentStorageOptions>(configuration.GetSection(AgentStorageOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.Configure<ToolAuthOptions>(configuration.GetSection(ToolAuthOptions.SectionName));
        services.Configure<GuardrailOptions>(configuration.GetSection(GuardrailOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        services.AddSingleton<IConfirmationStore, RedisConfirmationStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        services.AddSingleton<IAgentSessionStore, RedisAgentSessionStore>();

        services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();
        services.AddSingleton<IToolPolicy, CompositeToolPolicy>();
        services.AddSingleton<IReplanner>(sp =>
            new CompositeReplanner(sp.GetServices<IPluginReplanner>()));
        services.AddSingleton<IToolContractValidator, StayOta.Agent.Security.ToolContractValidator>();
        services.AddSingleton<IToolAuthorization, StayOta.Agent.Security.ClaimToolAuthorization>();
        services.AddSingleton<IGuardrail, StayOta.Agent.Security.BlockedPhraseGuardrail>();
        services.AddSingleton<IGuardrailPipeline, StayOta.Agent.Security.GuardrailPipeline>();

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddScoped<DeterministicTurnContext>();
        services.AddScoped<TurnHitlOptions>();
        services.AddScoped<DeterministicRefundChatClient>();
        services.AddScoped<IChatClient>(sp =>
        {
            var hostOpts = sp.GetRequiredService<IOptions<HostingOptions>>().Value;
            var factory = sp.GetRequiredService<IChatClientFactory>();
            var provider = factory.ProviderName;
            if (provider is "openai" or "ollama")
            {
                try
                {
                    return factory.CreateRemote();
                }
                catch (Exception ex)
                {
                    if (!hostOpts.AllowDeterministicFallback)
                        throw;

                    var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("ChatClientRegistration");
                    logger?.LogWarning(ex, "Falling back to DeterministicRefundChatClient");
                    return sp.GetRequiredService<DeterministicRefundChatClient>();
                }
            }

            return sp.GetRequiredService<DeterministicRefundChatClient>();
        });

        services.AddScoped<IAgentHost, ChatClientAgentHost>();
        services.AddScoped<IAgentConversationService, AgentConversationService>();
        services.AddScoped<IAgentToolCatalog, CompositeAgentToolCatalog>();
        services.AddSingleton<RuntimeAiOptions>();

        return services;
    }
}
