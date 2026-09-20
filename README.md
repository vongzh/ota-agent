# StayOTA Agent（通用酒店 OTA Agent）

本仓库是**独立的酒店 OTA 通用 Agent**（StayOTA Agent）：面向酒店订单与售后场景的受控 AI Agent 运行时。框架层提供意图理解、政策检索、规则/风险、Tool 写门禁、Workflow、Session/审计与离线 Eval；**业务能力以插件垂直包扩展**，不绑定单一售后类型。

当前内置演示垂直为 **退款 / 取消**（`Plugins.Refund`，含 A–L 场景与 33 Tool），并附带可运行的 `Plugins.Echo` 样例，用于验证多插件挂载。后续可继续扩展改期、发票、投诉等 OTA 能力。

技术栈：**.NET 10 + Microsoft.Extensions.AI + Microsoft Agent Framework + Vue3（Ant Design Vue Demo 壳）+ PostgreSQL + Redis**

## 架构要点

| 层 | 实现 |
| --- | --- |
| 模块布局 | `StayOta.Agent.Abstractions` + `StayOta.Agent` + 垂直插件（如 `Plugins.Refund` / `Plugins.Echo`）+ `StayOta.Agent.Host` |
| 插件模型 | `IAgentPlugin` + `AddAgentPlugin<T>`；Tool / Policy / Planner 由插件贡献，Host 做组合 |
| 模型接入 | `IChatClient`（默认 `DeterministicChatClient` + 插件 `IDeterministicIntentPlanner`，可换成 Azure OpenAI / Foundry） |
| Agent | `ChatClientAgent`（`Microsoft.Agents.AI`） |
| 场景编排 | `WorkflowBuilder` + `InProcessExecution`（`Microsoft.Agents.AI.Workflows`）；演示含退款 A–L |
| Tool | `AIFunctionFactory` + `ApprovalRequiredAIFunction`（确认类写操作） |
| FunctionApproval | `ToolApprovalRequestContent` → `POST /api/agent/approvals` |
| MCP | `MapMcp("/mcp")`；插件可暴露工具；`Production:Mode=Mcp` 可拉外部工具 |
| 生产直连 | `Production:Mode=Mock\|Http\|Mcp`（Http 走 BaseUrl 订单/政策 API） |
| 领域门禁 | `ToolGateway`（确认令牌 / 版本 / 幂等 / 审计） |
| 规则 / 风险 / Eval | Domain + Verifier（随垂直插件） |
| PG 隔离 | `AgentStorage:Schema=agent`（默认同库 schema 隔离，与垂直业务解耦） |

## 已覆盖能力

- 通用运行时：ChatClientAgent、多轮 Session、FunctionApproval HITL、流式 SSE、插件组合与 NuGet pack
- 退款演示垂直：A–L 十二场景 + 边界态；33 Tool 契约与 Mock 执行（确认令牌 / 版本 / 幂等 / 审计）
- 意图 · 槽位 · 政策检索 Top3 · 规则引擎 · 风险分层（退款插件）
- Session（Redis）/ Case Event / Workflow Trace（PostgreSQL，schema=`agent`）
- 36 条离线 Eval（`POST /api/eval/run`）
- A–L Agent Framework Workflow（`POST /api/workflows/run-all`）
- Tool `allowed_conversation_states` 白名单门禁
- Verifier 决策/工作流断言
- 前端 Demo：Agent 设计 / 智能处理台 / 运营看板 / 框架调试台
- Deterministic / OpenAI / Ollama 可切换；Deterministic 可按用户话自主选 Tool
- Tool 失败重规划（门禁拒绝后回退候选 Tool）
- 政策检索：同义词 + 字符 bigram 相似 + 成交快照加权

## 启动

```bash
# 依赖：Postgres + Redis（可用 docker compose up -d）
export PATH="$HOME/.dotnet:$PATH"
cd backend
dotnet run --project src/Hosts/StayOta.Agent.Host --urls http://127.0.0.1:5088

cd ../frontend
npm install && npm run dev
```

- API Swagger: http://127.0.0.1:5088/swagger
- MCP: http://127.0.0.1:5088/mcp
- 前端: http://127.0.0.1:5173
- Health 会返回 `aiProvider` / `agent` / `productionMode` / `mcpEndpoint` / `pgSchema` / `stack`

## 测试

```bash
cd backend && dotnet test StayOta.Agent.slnx
# CI 同等：设置 STAYOTA_AGENT_ROOT 指向仓库根，加载 contracts/mock/eval
curl -X POST http://127.0.0.1:5088/api/eval/run
curl -X POST http://127.0.0.1:5088/api/workflows/run-all
```

GitHub Actions：`.github/workflows/ci.yml`（`dotnet test` + pack + `frontend` vitest/build + Playwright `e2e`）。

浏览器 e2e（自包含 mock，CI 默认同款）：

```bash
cd frontend && npm ci && npm run build && npx playwright install chromium && npm run test:e2e
```

对接真实 Host / docker-compose：见 [`frontend/e2e/README.md`](./frontend/e2e/README.md)。

可选容器（依赖 compose profile）：

```bash
docker compose --profile app up --build
```

## Production 配置

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Postgres='...'
export ConnectionStrings__Redis='...'
export Hosting__ApiKey='...'   # DemoEnabled=false 时必填，否则启动失败
export Production__Mode=Http
export Production__BaseUrl='https://orders.internal/'
# 可选：AgentStorage__Schema=agent
```

- `DemoEnabled=false`：禁止启动删库、`ResetDemo`、Eval/Workflow 演示端、开放确认签发；**且必须配置 `Hosting:ApiKey`**
- Http/Mcp：**不**静默回退 Mock；AI 失败不静默降级 Deterministic（除非显式允许）

与主站订单/鉴权等系统对接时，可参考可选说明 [`docs/STAYOTA-INTEGRATION.md`](./docs/STAYOTA-INTEGRATION.md)（非本仓运行前置依赖）。插件编写见 [`docs/PLUGIN-AUTHORING.md`](./docs/PLUGIN-AUTHORING.md)。

## AI 提供商

默认 `Deterministic`（离线演示）。可在 `appsettings.json` 或环境变量切换：

```bash
# OpenAI / 兼容网关
export AI_PROVIDER=OpenAI
export OPENAI_API_KEY=sk-...
# optional: OPENAI_ENDPOINT=https://...  OPENAI_MODEL=gpt-4o-mini

# Ollama 本地
export AI_PROVIDER=Ollama
export OLLAMA_ENDPOINT=http://127.0.0.1:11434
export OLLAMA_MODEL=llama3.2
```

Demo 下配置错误会回退 Deterministic；正式态（`AllowDeterministicFallback=false`）则启动失败，避免静默降级。
