using Microsoft.Extensions.AI;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;

namespace StayOta.Agent.Tools;

/// <summary>Merges all <see cref="IPluginToolCatalog"/> contributions into one host catalog.</summary>
public sealed class CompositeAgentToolCatalog(IEnumerable<IPluginToolCatalog> parts) : IAgentToolCatalog
{
    private readonly IPluginToolCatalog[] _parts = parts.ToArray();

    public IReadOnlyDictionary<string, AIFunction> Functions
    {
        get
        {
            var map = new Dictionary<string, AIFunction>(StringComparer.Ordinal);
            foreach (var part in _parts)
            {
                foreach (var kv in part.Functions)
                    map[kv.Key] = kv.Value;
            }

            return map;
        }
    }

    public IReadOnlyList<AITool> GetAiTools(bool requireApprovalForWrites = true)
    {
        var list = new List<AITool>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in _parts)
        {
            foreach (var tool in part.GetAiTools(requireApprovalForWrites))
            {
                if (seen.Add(tool.Name ?? ""))
                    list.Add(tool);
            }
        }

        return list;
    }

    public Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default)
    {
        var owner = _parts.FirstOrDefault(p => p.OwnsTool(call.ToolName))
                    ?? throw new InvalidOperationException($"No plugin owns tool '{call.ToolName}'");
        return owner.InvokeAsync(call, ct);
    }
}
