using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Tools;

/// <summary>Merges <see cref="IToolPolicyContribution"/> from all registered plugins.</summary>
public sealed class CompositeToolPolicy(IEnumerable<IToolPolicyContribution> contributions) : IToolPolicy
{
    private readonly IToolPolicyContribution[] _parts = contributions.ToArray();

    public bool IsWrite(string toolName) =>
        _parts.Any(p => p.WriteTools.Contains(toolName));

    public bool RequiresConfirmation(string toolName) =>
        _parts.Any(p => p.ConfirmRequired.Contains(toolName));

    public ToolAccess AccessOf(string toolName) =>
        IsWrite(toolName) ? ToolAccess.Write : ToolAccess.Read;

    public string StateFor(string toolName, string? scenarioId = null)
    {
        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var key = (scenarioId.ToUpperInvariant(), toolName);
            foreach (var part in _parts)
            {
                if (part.ScenarioOverrides.TryGetValue(key, out var over))
                    return over;
            }
        }

        foreach (var part in _parts)
        {
            if (part.DefaultStates.TryGetValue(toolName, out var state))
                return state;
        }

        return "DECISION_READY";
    }
}
