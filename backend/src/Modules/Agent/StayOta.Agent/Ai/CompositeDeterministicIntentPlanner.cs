using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Ai;

/// <summary>
/// Merges plugin planners: each plugin contributes picks for tools still available.
/// </summary>
public sealed class CompositeDeterministicIntentPlanner(IEnumerable<IDeterministicIntentPlanner> planners)
    : IDeterministicIntentPlanner
{
    private readonly IDeterministicIntentPlanner[] _planners = planners.ToArray();

    public string PluginId => "composite";

    public IReadOnlyList<string> Select(
        string message,
        string conversationState,
        IReadOnlyCollection<string> availableTools,
        string? preferredWriteTool = null)
    {
        if (_planners.Length == 0) return [];

        var available = new HashSet<string>(availableTools, StringComparer.Ordinal);
        var picks = new List<string>();

        foreach (var planner in _planners)
        {
            foreach (var tool in planner.Select(message, conversationState, available, preferredWriteTool: null))
            {
                if (available.Remove(tool) && !picks.Contains(tool, StringComparer.Ordinal))
                    picks.Add(tool);
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredWriteTool) &&
            availableTools.Contains(preferredWriteTool!) &&
            !picks.Contains(preferredWriteTool!, StringComparer.Ordinal))
        {
            picks.Add(preferredWriteTool!);
        }

        return picks.Take(8).ToList();
    }
}
