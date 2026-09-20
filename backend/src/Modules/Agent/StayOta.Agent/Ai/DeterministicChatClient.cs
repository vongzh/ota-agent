using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Ai;

/// <summary>
/// Offline / demo <see cref="IChatClient"/>. Turn plan comes from scoped
/// <see cref="DeterministicTurnContext"/>; autonomous tool picks come from
/// plugin-registered <see cref="IDeterministicIntentPlanner"/> instances.
/// </summary>
public sealed class DeterministicChatClient(
    DeterministicTurnContext turnContext,
    IDeterministicIntentPlanner intentPlanner) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("deterministic", new Uri("local://stayota-agent"));

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        if (serviceType == typeof(ChatClientMetadata)) return Metadata;
        if (serviceType == typeof(DeterministicChatClient)) return this;
        // Backward-compat service lookup used by older tests/hosts.
        if (serviceType.Name == "DeterministicRefundChatClient") return this;
        return null;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var completed = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(f => f.CallId).ToHashSet(StringComparer.Ordinal);
        var tools = options?.Tools?.OfType<AIFunction>()
                        .GroupBy(t => t.Name, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                    ?? new Dictionary<string, AIFunction>(StringComparer.Ordinal);
        var toolNames = options?.Tools?
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var planned = ResolvePlannedTools(toolNames);

        foreach (var toolName in planned)
        {
            var callId = $"call_{toolName}";
            if (completed.Contains(callId)) continue;
            if (!tools.ContainsKey(toolName) && !toolNames.Contains(toolName))
                continue;

            return Task.FromResult(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent(callId, toolName, new Dictionary<string, object?>())
                ])
            ]));
        }

        var reply = turnContext.Plan?.SuggestedReply
                    ?? "助手已完成本轮决策（确定性 ChatClient，可替换为 Azure OpenAI / Foundry）。";
        return Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, reply)
        ]));
    }

    private IReadOnlyList<string> ResolvePlannedTools(IReadOnlyCollection<string> available)
    {
        var plan = turnContext.Plan;
        if (plan is null) return [];

        var planned = new List<string>();

        if (plan.HintTools is { Count: > 0 })
        {
            planned.AddRange(plan.HintTools.Where(available.Contains).Distinct(StringComparer.Ordinal));
        }
        else if (plan.AllowAutonomousToolSelection)
        {
            planned.AddRange(intentPlanner.Select(
                plan.UserMessage,
                plan.ConversationState,
                available,
                preferredWriteTool: null));
        }

        if (!string.IsNullOrWhiteSpace(plan.PreferredWriteTool) &&
            available.Contains(plan.PreferredWriteTool!) &&
            !planned.Contains(plan.PreferredWriteTool!, StringComparer.Ordinal))
        {
            planned.Add(plan.PreferredWriteTool!);
        }

        return planned.Take(8).ToList();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var message in response.Messages)
        {
            var textParts = message.Contents.OfType<TextContent>().ToList();
            var other = message.Contents.Where(c => c is not TextContent).ToList();

            foreach (var content in other)
                yield return new ChatResponseUpdate(message.Role, [content]);

            foreach (var text in textParts)
            {
                var value = text.Text ?? "";
                const int chunk = 8;
                for (var i = 0; i < value.Length; i += chunk)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var piece = value[i..Math.Min(i + chunk, value.Length)];
                    yield return new ChatResponseUpdate(message.Role, [new TextContent(piece)]);
                    await Task.Yield();
                }
            }
        }
    }
}

/// <summary>Obsolete alias — use <see cref="DeterministicChatClient"/>.</summary>
[Obsolete("Use DeterministicChatClient; refund intent planning lives in Plugins.Refund.")]
public sealed class DeterministicRefundChatClient : IChatClient
{
    private readonly DeterministicChatClient _inner;

    public DeterministicRefundChatClient(
        DeterministicTurnContext turnContext,
        IDeterministicIntentPlanner? intentPlanner = null)
    {
        _inner = new DeterministicChatClient(
            turnContext,
            intentPlanner ?? new CompositeDeterministicIntentPlanner([]));
    }

    public ChatClientMetadata Metadata => _inner.Metadata;
    public void Dispose() => _inner.Dispose();
    public object? GetService(Type serviceType, object? key = null) => _inner.GetService(serviceType, key);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetStreamingResponseAsync(messages, options, cancellationToken);
}
