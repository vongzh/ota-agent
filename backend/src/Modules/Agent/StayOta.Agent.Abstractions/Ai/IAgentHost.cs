using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using StayOta.Agent.Abstractions.Contracts;

namespace StayOta.Agent.Abstractions.Ai;

/// <summary>Framework-level ChatClientAgent host (vertical-agnostic).</summary>
public interface IAgentHost
{
    AIAgent Agent { get; }
    IChatClient ChatClient { get; }
    string ProviderName { get; }
}

/// <summary>MEAI tool catalog exposed by a vertical plugin (or composite).</summary>
public interface IAgentToolCatalog
{
    IReadOnlyList<AITool> GetAiTools(bool requireApprovalForWrites = true);
    IReadOnlyDictionary<string, AIFunction> Functions { get; }
    Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default);
}

/// <summary>
/// Per-request HITL switch: when false, confirm-required tools are not wrapped in ApprovalRequired
/// (used after business confirmation so Agent→Gateway remains the sole write surface).
/// </summary>
public sealed class TurnHitlOptions
{
    public bool RequireFunctionApproval { get; set; } = true;
}
