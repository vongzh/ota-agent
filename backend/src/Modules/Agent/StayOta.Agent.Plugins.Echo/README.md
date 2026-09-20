# Authoring an Agent plugin

## Contract

Implement `StayOta.Agent.Abstractions.Plugins.IAgentPlugin`:

| Member | Purpose |
| --- | --- |
| `Id` / `DisplayName` | Registry identity (shown on `/health`, `/api/plugins`) |
| `ToolPolicy` | `IToolPolicyContribution` — write/confirm/state tables for **your** tools only |
| `AgentInstructions` / `AgentName` | Used when your plugin is **primary** (first registered) |
| `ConfigureServices` | Register `IPluginToolCatalog`, optional MCP/Eval/EF |

## Host wiring

```csharp
builder.Services.AddStayOtaAgent(builder.Configuration);
builder.Services.AddAgentPlugin<RefundAgentPlugin>(builder.Configuration); // primary
builder.Services.AddAgentPlugin<EchoAgentPlugin>(builder.Configuration);   // sample
```

`CompositeToolPolicy` merges all contributions. `CompositeAgentToolCatalog` merges all `IPluginToolCatalog`s. `ChatClientAgentHost` reads the **primary** plugin for instructions.

## Samples in this repo

| Plugin | Path | Tools |
| --- | --- | --- |
| Refund (full vertical) | `StayOta.Agent.Plugins.Refund/` | 33 refund tools via Gateway |
| Echo (runnable sample) | `EchoAgentPlugin.cs` | `echo_ping`, `echo_reflect` (+ MCP) |

Pack: `bash scripts/pack.sh` — see root `docs/PLUGIN-AUTHORING.md`.

## Rules of thumb

1. Do **not** put tool name lists in Abstractions — contribute via `ToolPolicy`.
2. Tool execution must go **Agent → composite catalog → plugin catalog / Gateway** (no orchestrator bypass).
3. Writes that need HITL: FunctionApproval is the sole user-facing approval surface.
