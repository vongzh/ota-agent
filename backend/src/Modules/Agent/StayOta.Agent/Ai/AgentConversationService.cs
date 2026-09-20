using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Tools;

namespace StayOta.Agent.Ai;

public sealed class AgentConversationService(
    IAgentHost agentHost,
    IAgentSessionStore sessionStore,
    DeterministicTurnContext turnContext,
    TurnHitlOptions hitlOptions,
    IToolPolicy toolPolicy,
    ILogger<AgentConversationService> logger) : IAgentConversationService
{
    public async Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct = default)
    {
        // Drive ApprovalRequired wrapping before ChatClientAgentHost lazily builds Agent.
        hitlOptions.RequireFunctionApproval = request.RequireWriteApproval;

        using var _ = ToolInvocationContext.Push(new ToolInvocationContext
        {
            TraceId = request.TraceId,
            UserId = request.UserId,
            OrderId = request.OrderId,
            CaseId = request.CaseId,
            ScenarioId = request.ScenarioId,
            RiskLevel = request.RiskLevel,
            ConversationState = request.ConversationState,
            Arguments = request.AmbientArguments,
            ConfirmationToken = request.ConfirmationToken,
            IdempotencyKey = request.IdempotencyKey,
            ExpectedOrderVersion = request.ExpectedOrderVersion,
            Access = ToolAccess.Read,
            Policy = toolPolicy
        });

        // When RequireWriteApproval: strip confirm tools (HITL via PreferredWriteTool).
        // When user already confirmed: keep confirm tools in hints so Agent→Gateway executes them.
        var hints = request.HintTools
            .Where(t => !request.RequireWriteApproval || !toolPolicy.RequiresConfirmation(t))
            .Where(t => !request.RequireWriteApproval || t != request.WriteToolName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        turnContext.Set(new DeterministicTurnPlan
        {
            HintTools = hints,
            SuggestedReply = request.SuggestedReply,
            UserMessage = request.Message,
            ConversationState = request.ConversationState,
            PreferredWriteTool = request.RequireWriteApproval ? request.WriteToolName : null,
            AllowAutonomousToolSelection = request.AllowAutonomousToolSelection && hints.Count == 0
        });

        try
        {
            return await RunCoreAsync(request, ct);
        }
        finally
        {
            turnContext.Clear();
        }
    }

    private async Task<AgentTurnResult> RunCoreAsync(AgentTurnRequest request, CancellationToken ct)
    {
        AgentSession? session;
        string sessionId;
        AgentSessionSnapshot? prior = null;

        if (!string.IsNullOrWhiteSpace(request.ExistingSessionId))
        {
            prior = await sessionStore.GetAsync(request.ExistingSessionId!, ct);
            if (prior is not null && !string.IsNullOrWhiteSpace(prior.SessionJson))
            {
                session = await agentHost.Agent.DeserializeSessionAsync(
                    JsonDocument.Parse(prior.SessionJson).RootElement, cancellationToken: ct);
                sessionId = request.ExistingSessionId!;
                logger.LogInformation("Resuming agent session {Session}", sessionId);
            }
            else
            {
                session = await agentHost.Agent.CreateSessionAsync(ct);
                sessionId = $"ags_{Guid.NewGuid():N}"[..20];
                logger.LogWarning("Agent session {Session} missing; started new session {New}",
                    request.ExistingSessionId, sessionId);
            }
        }
        else
        {
            session = await agentHost.Agent.CreateSessionAsync(ct);
            sessionId = $"ags_{Guid.NewGuid():N}"[..20];
        }

        AgentResponse response;
        try
        {
            response = await agentHost.Agent.RunAsync(request.Message, session, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent turn failed; falling back to suggested reply");
            return new AgentTurnResult(sessionId, request.SuggestedReply, false, [], [], false);
        }

        var pending = ExtractApprovals(response);
        var toolsInvoked = ExtractInvokedTools(response);
        var reply = string.IsNullOrWhiteSpace(response.Text) ? request.SuggestedReply : response.Text;

        var sessionJson = await agentHost.Agent.SerializeSessionAsync(session, cancellationToken: ct);
        var snapshot = prior ?? new AgentSessionSnapshot();
        snapshot.SessionJson = sessionJson.GetRawText();
        snapshot.TraceId = request.TraceId;
        snapshot.UserId = request.UserId;
        snapshot.OrderId = request.OrderId;
        snapshot.CaseId = request.CaseId;
        snapshot.ScenarioId = request.ScenarioId;
        snapshot.RiskLevel = request.RiskLevel.ToString();
        snapshot.ConversationState = request.ConversationState;
        snapshot.ConfirmationToken = request.ConfirmationToken;
        snapshot.IdempotencyKey = request.IdempotencyKey;
        snapshot.ExpectedOrderVersion = request.ExpectedOrderVersion;
        snapshot.AmbientArguments = new Dictionary<string, object?>(request.AmbientArguments);
        snapshot.PendingApprovals = pending.Select(p => new PendingApprovalRecord
        {
            RequestId = p.RequestId,
            CallId = p.CallId,
            ToolName = p.ToolName,
            Arguments = p.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList();
        await sessionStore.SaveAsync(sessionId, snapshot, ct);

        logger.LogInformation(
            "Agent turn session={Session} resumed={Resumed} pendingApprovals={Count} tools={Tools}",
            sessionId, prior is not null, pending.Count, string.Join(',', toolsInvoked));

        return new AgentTurnResult(sessionId, reply, pending.Count > 0, pending, toolsInvoked, true);
    }

    public async Task<AgentTurnResult> RespondToApprovalAsync(ApprovalResponseRequest request, CancellationToken ct = default)
    {
        var snapshot = await sessionStore.GetAsync(request.SessionId, ct)
                       ?? throw new InvalidOperationException("agent session not found or expired");

        var pending = snapshot.PendingApprovals.FirstOrDefault(p => p.RequestId == request.RequestId)
                      ?? throw new InvalidOperationException($"approval request {request.RequestId} not found");

        hitlOptions.RequireFunctionApproval = true;

        using var _ = ToolInvocationContext.Push(new ToolInvocationContext
        {
            TraceId = snapshot.TraceId,
            UserId = snapshot.UserId,
            OrderId = snapshot.OrderId,
            CaseId = snapshot.CaseId,
            ScenarioId = snapshot.ScenarioId,
            RiskLevel = Enum.TryParse<RiskLevel>(snapshot.RiskLevel, out var rl) ? rl : RiskLevel.L1,
            ConversationState = snapshot.ConversationState,
            Arguments = snapshot.AmbientArguments,
            ConfirmationToken = snapshot.ConfirmationToken,
            IdempotencyKey = snapshot.IdempotencyKey,
            ExpectedOrderVersion = snapshot.ExpectedOrderVersion,
            Access = ToolAccess.Write,
            Policy = toolPolicy
        });

        var session = await agentHost.Agent.DeserializeSessionAsync(
            JsonDocument.Parse(snapshot.SessionJson).RootElement, cancellationToken: ct);

        var functionCall = new FunctionCallContent(
            pending.CallId,
            pending.ToolName,
            pending.Arguments.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value ?? "")));
        var approvalRequest = new ToolApprovalRequestContent(pending.RequestId, functionCall);
        var approvalMessage = new ChatMessage(ChatRole.User,
            [approvalRequest.CreateResponse(request.Approved, request.Reason ?? (request.Approved ? "user approved" : "user rejected"))]);

        var replyFallback = request.Approved
            ? $"已批准执行 {pending.ToolName}，业务写操作已提交。"
            : $"已拒绝执行 {pending.ToolName}，未改变业务状态。";

        turnContext.Set(new DeterministicTurnPlan
        {
            HintTools = [],
            SuggestedReply = replyFallback,
            AllowAutonomousToolSelection = false,
            UserMessage = "",
            ConversationState = snapshot.ConversationState
        });

        try
        {
            var response = await agentHost.Agent.RunAsync(approvalMessage, session, cancellationToken: ct);
            var nextPending = ExtractApprovals(response);
            var toolsInvoked = ExtractInvokedTools(response);
            var reply = string.IsNullOrWhiteSpace(response.Text) ? replyFallback : response.Text;

            var sessionJson = await agentHost.Agent.SerializeSessionAsync(session, cancellationToken: ct);
            snapshot.SessionJson = sessionJson.GetRawText();
            snapshot.PendingApprovals = nextPending.Select(p => new PendingApprovalRecord
            {
                RequestId = p.RequestId,
                CallId = p.CallId,
                ToolName = p.ToolName,
                Arguments = p.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList();
            await sessionStore.SaveAsync(request.SessionId, snapshot, ct);

            return new AgentTurnResult(request.SessionId, reply, nextPending.Count > 0, nextPending, toolsInvoked, true);
        }
        finally
        {
            turnContext.Clear();
        }
    }

    private static List<PendingToolApprovalDto> ExtractApprovals(AgentResponse response)
    {
        var list = new List<PendingToolApprovalDto>();
        foreach (var content in response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>())
        {
            if (content.ToolCall is not FunctionCallContent call) continue;
            var args = call.Arguments?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, object?>();
            list.Add(new PendingToolApprovalDto(
                content.RequestId,
                call.CallId ?? content.RequestId,
                call.Name ?? "unknown",
                args,
                $"批准执行 Tool `{call.Name}`（官方 ToolApprovalRequestContent）"));
        }
        return list;
    }

    private static List<string> ExtractInvokedTools(AgentResponse response)
    {
        return response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => c.Name ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
    }
}
