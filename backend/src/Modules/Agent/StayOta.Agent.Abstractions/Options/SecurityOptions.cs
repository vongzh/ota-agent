namespace StayOta.Agent.Abstractions.Options;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>When false, middleware is a no-op.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max requests per window per client key (API key or IP).</summary>
    public int RequestsPerMinute { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class ToolAuthOptions
{
    public const string SectionName = "ToolAuth";

    /// <summary>
    /// When false (default in demo), authorization is permissive.
    /// When true, caller roles must include agent:read / agent:write / agent:admin.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Role → tool name allow-list. Empty = role grants by access class only.</summary>
    public Dictionary<string, string[]> RoleToolAllowList { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GuardrailOptions
{
    public const string SectionName = "Guardrails";

    public bool Enabled { get; set; } = true;

    /// <summary>Block user messages / tool args containing these substrings (case-insensitive).</summary>
    public string[] BlockedPhrases { get; set; } = ["ignore previous instructions", "exfiltrate"];
}
