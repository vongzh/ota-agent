using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StayOta.Agent;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Abstractions.Plugins;
using StayOta.Agent.Ai;
using StayOta.Agent.Diagnostics;
using StayOta.Agent.Host.Security;
using StayOta.Agent.Plugins;
using StayOta.Agent.Plugins.Echo;
using StayOta.Agent.Plugins.Refund;
using StayOta.Agent.Plugins.Refund.Persistence;
using StayOta.Agent.Plugins.Refund.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Host is at src/Hosts/StayOta.Agent.Host → stayota-agent root is 4 levels up
var agentRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../../.."));
Environment.SetEnvironmentVariable("STAYOTA_AGENT_ROOT", agentRoot);

if (builder.Environment.IsProduction())
{
    builder.Services.PostConfigure<HostingOptions>(o =>
    {
        if (!builder.Configuration.GetSection(HostingOptions.SectionName).Exists())
        {
            o.DemoEnabled = false;
            o.ResetDatabaseOnStartup = false;
            o.AllowDeterministicFallback = false;
            o.ExposeDetailedHealth = false;
        }
    });
}

builder.Services.AddStayOtaAgent(builder.Configuration);
builder.Services.AddAgentPlugin<RefundAgentPlugin>(builder.Configuration);
builder.Services.AddAgentPlugin<EchoAgentPlugin>(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(AgentTelemetry.ServiceName))
    .WithTracing(t =>
    {
        t.AddSource(AgentTelemetry.ServiceName);
        t.AddAspNetCoreInstrumentation();
    })
    .WithMetrics(m =>
    {
        m.AddMeter(AgentTelemetry.ServiceName);
        m.AddAspNetCoreInstrumentation();
    });

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var hostingPreview = builder.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.AllowAnyHeader().AllowAnyMethod();
    if (hostingPreview.DemoEnabled && hostingPreview.AllowedOrigins.Length == 0)
        p.AllowAnyOrigin();
    else if (hostingPreview.AllowedOrigins.Length > 0)
        p.WithOrigins(hostingPreview.AllowedOrigins).AllowCredentials();
    else
        p.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173");
}));

var app = builder.Build();
var hosting = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;
HostingGuards.Validate(hosting);

if (!string.IsNullOrWhiteSpace(hosting.PathBase))
{
    app.UsePathBase(hosting.PathBase.TrimEnd('/'));
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var schemaSql = QuotePgIdent(db.Schema);
    if (schemaSql is not null)
    {
#pragma warning disable EF1002 // schemaSql is regex-validated + quoted identifier only
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {schemaSql}");
#pragma warning restore EF1002
    }

    if (hosting.ResetDatabaseOnStartup)
    {
        await db.Database.EnsureDeletedAsync();
        if (schemaSql is not null)
        {
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {schemaSql}");
#pragma warning restore EF1002
        }
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    if (hosting.SeedOnStartup)
    {
        var store = scope.ServiceProvider.GetRequiredService<IRefundDataStore>();
        await store.EnsureSeededAsync();
    }
}

if (hosting.DemoEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<CallerIdentityMiddleware>();
app.MapControllers();
app.MapMcp("/mcp");

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.MapGet("/health/ready", async (AppDbContext db, IConnectionMultiplexer redis) =>
{
    try
    {
        _ = await db.Scenarios.CountAsync();
        _ = await redis.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not_ready", error = ex.Message }, statusCode: 503);
    }
});

app.MapGet("/health", async (
    AppDbContext db,
    IConnectionMultiplexer redis,
    IToolGateway tools,
    IAgentToolCatalog aiTools,
    IAgentHost agentHost,
    IAgentPluginRegistry plugins,
    IChatClientFactory chatClientFactory,
    IOptions<AiOptions> aiOptions,
    IOptions<ProductionOptions> productionOptions,
    IOptions<HostingOptions> hostingOptions,
    IOptions<AgentStorageOptions> storageOptions,
    IProductionOrderClient production) =>
{
    var opts = hostingOptions.Value;
    bool pgOk;
    bool redisOk;
    int scenarios = 0;
    try
    {
        scenarios = await db.Scenarios.CountAsync();
        pgOk = true;
    }
    catch
    {
        pgOk = false;
    }

    try
    {
        await redis.GetDatabase().PingAsync();
        redisOk = true;
    }
    catch
    {
        redisOk = false;
    }

    var ready = pgOk && redisOk;
    var payload = new Dictionary<string, object?>
    {
        ["status"] = ready ? "ok" : "degraded",
        ["postgres"] = pgOk,
        ["redis"] = redisOk,
        ["demoEnabled"] = opts.DemoEnabled,
        ["productionMode"] = production.Mode,
        ["aiProvider"] = agentHost.ProviderName,
        ["agent"] = agentHost.Agent.Name,
        ["mcpEndpoint"] = "/mcp",
        ["authRequired"] = !string.IsNullOrWhiteSpace(opts.ApiKey),
        ["moduleLayout"] = "StayOta.Agent + IAgentPlugin (refund, echo)",
        ["plugins"] = plugins.Plugins.Select(p => new { p.Id, p.DisplayName }).ToArray(),
        ["pgSchema"] = storageOptions.Value.Schema,
        ["otel"] = new { service = AgentTelemetry.ServiceName, traces = true, metrics = true }
    };

    if (opts.ExposeDetailedHealth)
    {
        payload["scenarios"] = scenarios;
        payload["tools"] = tools.ListContracts().Count;
        payload["aiFunctions"] = aiTools.Functions.Count;
        payload["aiConfiguredProvider"] = aiOptions.Value.Provider;
        payload["aiResolvedProvider"] = chatClientFactory.ProviderName;
        payload["productionConfigured"] = productionOptions.Value.Mode;
        payload["stack"] = new
        {
            meai = "Microsoft.Extensions.AI",
            agentFramework = "Microsoft.Agents.AI",
            workflows = "Microsoft.Agents.AI.Workflows",
            openAi = "Microsoft.Extensions.AI.OpenAI",
            ollama = "OllamaSharp",
            mcp = "ModelContextProtocol.AspNetCore",
            functionApproval = "ApprovalRequiredAIFunction / ToolApprovalRequestContent"
        };
        payload["database"] = "postgresql";
        payload["cache"] = "redis";
        payload["agentRoot"] = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
    }

    return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
});

app.Run();

static string? QuotePgIdent(string? ident)
{
    if (string.IsNullOrWhiteSpace(ident)) return null;
    // Allow only safe PG identifiers (letters, digits, underscore).
    if (!System.Text.RegularExpressions.Regex.IsMatch(ident, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        throw new InvalidOperationException($"AgentStorage:Schema '{ident}' is not a valid PostgreSQL identifier");
    return $"\"{ident}\"";
}

public partial class Program;
