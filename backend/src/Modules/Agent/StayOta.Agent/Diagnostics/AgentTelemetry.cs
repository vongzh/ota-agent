using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StayOta.Agent.Diagnostics;

/// <summary>Minimal OpenTelemetry ActivitySource + Meter for the agent runtime.</summary>
public static class AgentTelemetry
{
    public const string ServiceName = "StayOta.Agent";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> Turns =
        Meter.CreateCounter<long>("agent.turns", description: "Agent conversation turns");

    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>("agent.tool_invocations", description: "ToolGateway invocations");

    public static readonly Counter<long> Approvals =
        Meter.CreateCounter<long>("agent.approvals", description: "FunctionApproval responses");
}
