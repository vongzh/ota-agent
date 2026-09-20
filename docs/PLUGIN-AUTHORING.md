# Authoring an Agent plugin

See also: `backend/src/Modules/Agent/StayOta.Agent.Plugins.Echo/README.md`

## Quick start

1. Implement `IAgentPlugin` (+ `IToolPolicyContribution` for your tools).
2. In `ConfigureServices`, register an `IPluginToolCatalog` (merged by the host into `IAgentToolCatalog`), plus optional MCP/Eval/EF.
3. Host:

```csharp
builder.Services.AddStayOtaAgent(configuration);
builder.Services.AddAgentPlugin<YourPlugin>(configuration);
```

## Packages (NuGet)

Local / CI pack (does **not** push):

```bash
bash scripts/pack.sh
# → artifacts/nuget/StayOta.Agent.Abstractions.*.nupkg
# → artifacts/nuget/StayOta.Agent.*.nupkg
# → artifacts/nuget/StayOta.Agent.Plugins.Echo.*.nupkg
```

| Package | Role |
| --- | --- |
| `StayOta.Agent.Abstractions` | Contracts, `IAgentPlugin`, tool policy contributions |
| `StayOta.Agent` | Runtime: conversation, Redis sessions, composite catalog |
| `StayOta.Agent.Plugins.Echo` | Runnable sample second vertical |

## In-repo plugins

| Id | Project | Role |
| --- | --- | --- |
| `refund` | `StayOta.Agent.Plugins.Refund` | Full hotel-refund vertical (primary) |
| `echo` | `StayOta.Agent.Plugins.Echo` | Runnable sample (`echo_ping` / `echo_reflect` + MCP) |

`GET /health` and `GET /api/plugins` list registered plugins. The Demo **框架调试台** (`/console`) covers sessions, audits, MCP try-invoke, and provider switch.
