using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Abstractions.Tools;

/// <summary>Plugin-contributed write/confirm/state tables (no vertical hardcoding in framework).</summary>
public interface IToolPolicyContribution
{
    IReadOnlySet<string> WriteTools { get; }
    IReadOnlySet<string> ConfirmRequired { get; }
    IReadOnlyDictionary<string, string> DefaultStates { get; }
    IReadOnlyDictionary<(string Scenario, string Tool), string> ScenarioOverrides { get; }
}

/// <summary>Merged tool policy resolved from all registered <see cref="IToolPolicyContribution"/>s.</summary>
public interface IToolPolicy
{
    bool IsWrite(string toolName);
    bool RequiresConfirmation(string toolName);
    ToolAccess AccessOf(string toolName);
    string StateFor(string toolName, string? scenarioId = null);
}

/// <summary>Mutable builder used by plugins to declare policy.</summary>
public class ToolPolicyContribution : IToolPolicyContribution
{
    public HashSet<string> WriteTools { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ConfirmRequired { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> DefaultStates { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string Scenario, string Tool), string> ScenarioOverrides { get; } = new();

    IReadOnlySet<string> IToolPolicyContribution.WriteTools => WriteTools;
    IReadOnlySet<string> IToolPolicyContribution.ConfirmRequired => ConfirmRequired;
    IReadOnlyDictionary<string, string> IToolPolicyContribution.DefaultStates => DefaultStates;
    IReadOnlyDictionary<(string Scenario, string Tool), string> IToolPolicyContribution.ScenarioOverrides => ScenarioOverrides;
}
