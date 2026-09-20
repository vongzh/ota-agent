namespace StayOta.Agent.Abstractions.Options;

/// <summary>
/// Demo vs production hosting switches. Production defaults: Demo disabled.
/// </summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>When true, allow ResetDemo, eval/workflow demos, open confirmation minting, Swagger.</summary>
    public bool DemoEnabled { get; set; } = true;

    /// <summary>Wipe + recreate DB on API boot (demo only). Never enable in Production.</summary>
    public bool ResetDatabaseOnStartup { get; set; } = false;

    /// <summary>Seed A–L fixtures when DB empty.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Shared API / MCP key. Empty = auth disabled (demo only).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>CORS origins. Empty + DemoEnabled = AllowAnyOrigin.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Expose detailed /health payload (stack, agentRoot). Prefer false in Production.</summary>
    public bool ExposeDetailedHealth { get; set; } = true;

    /// <summary>When Ai provider fails, fall back to Deterministic. Production should set false.</summary>
    public bool AllowDeterministicFallback { get; set; } = true;

    /// <summary>
    /// Optional path base when mounted inside StayOTA host (e.g. /ota-agent).
    /// Empty = root. Affects MapControllers / MapMcp relative to host.
    /// </summary>
    public string PathBase { get; set; } = "";

    /// <summary>Public module id for health/docs; controllers stay at /api.</summary>
    public string ModuleId { get; set; } = "stayota-agent";
}
