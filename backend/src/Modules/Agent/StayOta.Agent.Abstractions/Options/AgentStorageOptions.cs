namespace StayOta.Agent.Abstractions.Options;

/// <summary>
/// PostgreSQL storage isolation when sharing a StayOTA database.
/// </summary>
public sealed class AgentStorageOptions
{
    public const string SectionName = "AgentStorage";

    /// <summary>PG schema for Agent runtime tables (default <c>agent</c>). Empty = public. Not tied to a vertical plugin.</summary>
    public string Schema { get; set; } = "agent";
}
