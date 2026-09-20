# Authoring an Agent plugin

See also: `backend/src/Modules/Agent/StayOta.Agent.Plugins.Echo/README.md`

## Quick start

1. Implement `IAgentPlugin` (+ `IToolPolicyContribution` for your tools).
2. In `ConfigureServices`, register `IAgentToolCatalog`, `IToolGateway`, orchestrator, optional MCP/Eval.
3. Host:

```csharp
builder.Services.AddStayOtaAgent(configuration);
builder.Services.AddAgentPlugin<YourPlugin>(configuration);
```

## In-repo plugins

| Id | Project | Role |
| --- | --- | --- |
| `refund` | `StayOta.Agent.Plugins.Refund` | Full hotel-refund vertical (primary) |
| `echo` | `StayOta.Agent.Plugins.Echo` | Stub second vertical for framework authors |

`GET /health` lists registered plugins under `plugins`.
