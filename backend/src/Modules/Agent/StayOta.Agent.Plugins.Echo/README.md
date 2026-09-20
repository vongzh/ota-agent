# Authoring an Agent plugin

## Contract

Implement `StayOta.Agent.Abstractions.Plugins.IAgentPlugin`:

| Member | Purpose |
| --- | --- |
| `Id` / `DisplayName` | Registry identity (shown on `/health`) |
| `ToolPolicy` | `IToolPolicyContribution` — write/confirm/state tables for **your** tools only |
| `AgentInstructions` / `AgentName` | Used when your plugin is **primary** (first registered) |
| `ConfigureServices` | Register gateway, catalog (`IAgentToolCatalog`), orchestrator, MCP, eval, EF, … |

## Host wiring

```csharp
builder.Services.AddStayOtaAgent(builder.Configuration);
builder.Services.AddAgentPlugin<RefundAgentPlugin>(builder.Configuration); // primary
builder.Services.AddAgentPlugin<EchoAgentPlugin>(builder.Configuration);   // sample
```

`CompositeToolPolicy` merges all contributions. `ChatClientAgentHost` reads the **primary** plugin for instructions.

## Samples in this repo

| Plugin | Path |
| --- | --- |
| Refund (full vertical) | `StayOta.Agent.Plugins.Refund/RefundAgentPlugin.cs` |
| Echo (stub) | `StayOta.Agent.Plugins.Echo/EchoAgentPlugin.cs` |

## Rules of thumb

1. Do **not** put tool name lists in Abstractions — contribute via `ToolPolicy`.
2. Tool execution must go **Agent → `IAgentToolCatalog` → `IToolGateway`** (no orchestrator bypass).
3. Confirm-required writes: either FunctionApproval (`RequireWriteApproval`) or ambient confirmation token with `RequireFunctionApproval=false` on the turn.
