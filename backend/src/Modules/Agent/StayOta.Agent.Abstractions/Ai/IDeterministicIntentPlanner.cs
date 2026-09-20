namespace StayOta.Agent.Abstractions.Ai;

/// <summary>
/// Plugin-owned tool picker for the offline <c>DeterministicChatClient</c>.
/// Core does not hardcode vertical tool names; each plugin registers its own planner.
/// </summary>
public interface IDeterministicIntentPlanner
{
    string PluginId { get; }

    IReadOnlyList<string> Select(
        string message,
        string conversationState,
        IReadOnlyCollection<string> availableTools,
        string? preferredWriteTool = null);
}
