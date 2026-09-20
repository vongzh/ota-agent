using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Abstractions.Tools;
using StayOta.Agent.Plugins.Refund.Ai;
using StayOta.Agent.Plugins.Refund.Mcp;
using StayOta.Agent.Plugins.Refund.Persistence;
using StayOta.Agent.Plugins.Refund.Production;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Plugins.Refund;

/// <summary>Hotel refund vertical — registers via <c>AddAgentPlugin&lt;RefundAgentPlugin&gt;</c>.</summary>
public sealed class RefundAgentPlugin : IAgentPlugin
{
    public string Id => "refund";
    public string DisplayName => "StayOTA Hotel Refund";
    public IToolPolicyContribution ToolPolicy { get; } = new RefundToolPolicyContribution();

    public string AgentInstructions => """
        你是 StayOTA 酒店退款助手。遵循政策与风险分层，优先调用已注册工具完成查单、报价与受控写操作。
        高风险（L3）财务写操作必须人工确认；不得绕过确认门禁。
        写操作工具可能需要人工审批（ApprovalRequired）。
        """;

    public string AgentName => "stayota-refund-agent";
    public string AgentDescription => "Hotel refund agent powered by Microsoft.Extensions.AI + Agent Framework";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var hosting = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
        var isProductionLike = !hosting.DemoEnabled;

        var pg = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(pg))
        {
            if (isProductionLike)
                throw new InvalidOperationException("ConnectionStrings:Postgres is required when DemoEnabled=false");
            pg = "Host=127.0.0.1;Port=5432;Database=stayota_refund;Username=stayota;Password=stayota";
        }

        services.AddDbContext<AppDbContext>((_, opt) => opt.UseNpgsql(pg));

        services.AddHttpClient("production", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ProductionOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IRefundDataStore, RefundDataStore>();
        services.AddScoped<IScenarioCatalog, ScenarioCatalog>();
        services.AddSingleton<IIntentService, IntentService>();
        services.AddSingleton<IPolicyRetrieval, PolicyRetrieval>();
        services.AddScoped<IRulesEngine, RulesEngine>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddScoped<RefundAiToolCatalog>();
        services.AddScoped<IAgentToolCatalog>(sp => sp.GetRequiredService<RefundAiToolCatalog>());

        services.AddScoped<MockProductionOrderClient>();
        services.AddScoped<HttpProductionOrderClient>();
        services.AddScoped<McpProductionOrderClient>();
        services.AddScoped<IExternalMcpToolSource, ExternalMcpToolSource>();
        services.AddScoped<IProductionOrderClient>(sp =>
        {
            var mode = sp.GetRequiredService<IOptions<ProductionOptions>>().Value.Mode;
            return mode.ToLowerInvariant() switch
            {
                "http" => sp.GetRequiredService<HttpProductionOrderClient>(),
                "mcp" => sp.GetRequiredService<McpProductionOrderClient>(),
                _ => sp.GetRequiredService<MockProductionOrderClient>()
            };
        });

        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IScenarioWorkflow, ScenarioWorkflow>();
        services.AddSingleton<IVerifier, Verifier>();
        services.AddScoped<IEvalRunner, EvalRunner>();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<RefundMcpTools>();
    }
}

/// <summary>Backward-compatible alias — prefer <c>AddAgentPlugin&lt;RefundAgentPlugin&gt;</c>.</summary>
public static class RefundPluginServiceCollectionExtensions
{
    [Obsolete("Use services.AddAgentPlugin<RefundAgentPlugin>(configuration) instead.")]
    public static IServiceCollection AddRefundPlugin(this IServiceCollection services, IConfiguration configuration)
    {
        // Local duplicate of AddAgentPlugin to avoid circular project reference.
        var plugin = new RefundAgentPlugin();
        services.AddSingleton<IAgentPlugin>(plugin);
        services.AddSingleton<IToolPolicyContribution>(plugin.ToolPolicy);
        plugin.ConfigureServices(services, configuration);
        return services;
    }
}
