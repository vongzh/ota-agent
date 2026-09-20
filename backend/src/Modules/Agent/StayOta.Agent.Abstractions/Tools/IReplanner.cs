using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Abstractions.Tools;

/// <summary>Plugin-contributed failure recovery suggestions for the agent tool loop.</summary>
public interface IReplanner
{
    IReadOnlyList<ReplanSuggestion> Suggest(ReplanRequest request);
}

/// <summary>Marker for vertical replanners; host merges via <see cref="CompositeReplanner"/>.</summary>
public interface IPluginReplanner : IReplanner;

public sealed record ReplanRequest(
    string FailedTool,
    string? DenyReason,
    string DecisionAction,
    RiskLevel RiskLevel);

public sealed record ReplanSuggestion(string ToolName, string ConversationState, string Reason);

/// <summary>Composite: first plugin replanner that returns suggestions wins.</summary>
public sealed class CompositeReplanner(IEnumerable<IPluginReplanner> parts) : IReplanner
{
    private readonly IPluginReplanner[] _parts = parts.ToArray();

    public IReadOnlyList<ReplanSuggestion> Suggest(ReplanRequest request)
    {
        foreach (var part in _parts)
        {
            var batch = part.Suggest(request);
            if (batch.Count > 0) return batch;
        }

        return [];
    }
}
