namespace StayOta.Agent.Abstractions.Contracts;

/// <summary>Agent-side ops dashboard aggregate — process metrics only, not business north-stars.</summary>
public sealed record OpsSummaryDto(
    DateTimeOffset GeneratedAt,
    string Source,
    OpsNorthStarDto NorthStar,
    OpsBusinessPlaceholderDto BusinessNorthStar,
    IReadOnlyList<OpsMetricDto> Metrics,
    IReadOnlyList<OpsFunnelStageDto> Funnel,
    IReadOnlyList<OpsRiskItemDto> RiskItems,
    OpsSessionsDto Sessions,
    OpsAuditsDto Audits,
    OpsWorkflowsDto Workflows,
    OpsCasesDto Cases,
    OpsEvalDto Eval,
    OpsPluginsDto Plugins);

public sealed record OpsNorthStarDto(
    string Label,
    string Value,
    string Note,
    bool IsProcessMetric);

public sealed record OpsBusinessPlaceholderDto(
    string Label,
    string Status,
    string Note);

public sealed record OpsMetricDto(
    string Group,
    string Name,
    string Value,
    string Note,
    string Tone);

public sealed record OpsFunnelStageDto(
    string Stage,
    int In,
    int Drop,
    string Note);

public sealed record OpsRiskItemDto(
    string Type,
    int Count,
    string Risk,
    string Stage,
    string Owner);

public sealed record OpsSessionsDto(
    int Total,
    int WithPendingApproval,
    int PendingApprovalTotal);

public sealed record OpsAuditsDto(
    int Total,
    int Allowed,
    int Denied,
    double DenyRate);

public sealed record OpsWorkflowsDto(
    int Total,
    int Succeeded,
    int Failed,
    int Running);

public sealed record OpsCasesDto(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus);

public sealed record OpsEvalDto(int CaseCount);

public sealed record OpsPluginsDto(
    int Count,
    IReadOnlyList<OpsPluginItemDto> Items);

public sealed record OpsPluginItemDto(
    string Id,
    string DisplayName,
    bool IsPrimary);
