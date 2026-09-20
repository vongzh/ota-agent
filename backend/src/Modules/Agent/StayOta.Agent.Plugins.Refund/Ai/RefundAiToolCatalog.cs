using Microsoft.Extensions.AI;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Tools;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Plugins.Refund.Ai;

/// <summary>
/// Exposes domain tools as MEAI <see cref="AIFunction"/>s.
/// Confirm-required writes wrap with <see cref="ApprovalRequiredAIFunction"/> when requested.
/// Failure recovery uses injected <see cref="IReplanner"/>.
/// </summary>
public sealed class RefundAiToolCatalog : IPluginToolCatalog
{
    private readonly IToolGateway _gateway;
    private readonly IRefundDataStore _store;
    private readonly IToolPolicy _toolPolicy;
    private readonly IReplanner _replanner;
    private readonly Lazy<Dictionary<string, AIFunction>> _functions;

    public RefundAiToolCatalog(
        IToolGateway gateway,
        IRefundDataStore store,
        IToolPolicy toolPolicy,
        IReplanner replanner)
    {
        _gateway = gateway;
        _store = store;
        _toolPolicy = toolPolicy;
        _replanner = replanner;
        _functions = new Lazy<Dictionary<string, AIFunction>>(BuildFunctions);
    }

    public string PluginId => "refund";
    public IReadOnlyDictionary<string, AIFunction> Functions => _functions.Value;
    public bool OwnsTool(string toolName) => _functions.Value.ContainsKey(toolName);

    public IReadOnlyList<AITool> GetAiTools(bool requireApprovalForWrites = true)
    {
        if (!requireApprovalForWrites)
            return _functions.Value.Values.Cast<AITool>().ToList();

        return _functions.Value.Select(kv =>
            _toolPolicy.RequiresConfirmation(kv.Key)
                ? (AITool)new ApprovalRequiredAIFunction(kv.Value)
                : kv.Value).ToList();
    }

    public Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default)
    {
        using var _ = ToolInvocationContext.Push(call, _toolPolicy);
        return _gateway.InvokeAsync(call, ct);
    }

    private Dictionary<string, AIFunction> BuildFunctions()
    {
        var map = new Dictionary<string, AIFunction>(StringComparer.Ordinal);
        foreach (var contract in _store.GetToolContracts())
        {
            var name = contract.Name;
            var purpose = string.IsNullOrWhiteSpace(contract.Purpose) ? name : contract.Purpose;
            map[name] = AIFunctionFactory.Create(
                async (AIFunctionArguments args, CancellationToken ct) =>
                {
                    var ambient = ToolInvocationContext.Current
                                  ?? throw new InvalidOperationException($"No ToolInvocationContext for {name}");
                    var merged = new Dictionary<string, object?>(ambient.Arguments);
                    foreach (var kv in args)
                        merged[kv.Key] = kv.Value;

                    var call = ambient.ToToolCall(name) with { Arguments = merged };
                    var result = await _gateway.InvokeAsync(call, ct);
                    if (result.Allowed && result.Success)
                        return result.Data ?? new { ok = true };

                    var action = merged.TryGetValue("action", out var act) ? Convert.ToString(act) ?? "" : "";
                    var suggestions = _replanner.Suggest(new ReplanRequest(
                        name, result.DenyReason, action, ambient.RiskLevel));

                    foreach (var suggestion in suggestions.Take(2))
                    {
                        if (_toolPolicy.RequiresConfirmation(suggestion.ToolName))
                            continue;

                        var retry = ambient.ToToolCall(suggestion.ToolName) with
                        {
                            ConversationState = suggestion.ConversationState,
                            Arguments = merged,
                            ConfirmationToken = _toolPolicy.IsWrite(suggestion.ToolName)
                                ? ambient.ConfirmationToken
                                : null,
                            IdempotencyKey = _toolPolicy.IsWrite(suggestion.ToolName)
                                ? (ambient.IdempotencyKey ?? $"replan-{suggestion.ToolName}-{Guid.NewGuid():N}"[..28])
                                : null,
                            ExpectedOrderVersion = _toolPolicy.IsWrite(suggestion.ToolName)
                                ? ambient.ExpectedOrderVersion
                                : null
                        };

                        var retryResult = await _gateway.InvokeAsync(retry, ct);
                        if (retryResult.Allowed && retryResult.Success)
                        {
                            return new
                            {
                                replanned_from = name,
                                via = suggestion.ToolName,
                                reason = suggestion.Reason,
                                data = retryResult.Data ?? new { ok = true }
                            };
                        }
                    }

                    throw new InvalidOperationException(result.DenyReason ?? $"{name} denied");
                },
                name,
                purpose);
        }

        return map;
    }
}
