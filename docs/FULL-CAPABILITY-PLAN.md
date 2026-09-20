# StayOTA Agent 架构改造计划（MEAI + Agent Framework）

## 目标

打造**通用酒店 OTA Agent 运行时**（不绑定单一售后类型）；减少手写 Orchestrator / FSM / Tool 调度引擎，把编排与模型接入迁到微软官方栈，领域规则与门禁保留自研。退款是首个演示垂直插件，非产品边界。

## 能力矩阵（演示）

| 能力块 | 状态 |
| --- | --- |
| A–L 十二场景 + 边界态 | ✅ |
| 33 Tool 契约与执行 | ✅ AIFunction + ToolGateway |
| 意图/槽位/政策检索/规则/风险 | ✅ Domain |
| Tool 权限门 | ✅ ToolGateway |
| Session / Case / Trace | ✅ PG + Redis |
| 36 条离线 Eval | ✅ |
| A–L Workflow | ✅ `Microsoft.Agents.AI.Workflows` |
| ChatClientAgent | ✅ 默认确定性 Client |
| 真 LLM（OpenAI / Ollama） | ✅ `ChatClientFactory` 可切换；默认 Deterministic |
| FunctionApproval HITL | ✅ `ApprovalRequiredAIFunction` + `/api/agent/approvals` |
| MCP `/mcp` | ✅ `RefundMcpTools` |
| Production Mock\|Http\|Mcp | ✅ 客户端已注册（Demo 默认 Mock） |
| **插件模型 `IAgentPlugin`** | ✅ `AddAgentPlugin<T>`；Refund + Echo stub；`ToolPolicy` 插件贡献 |
| 官方 Vben monorepo | 🔜 本仓为 Ant Design Vue Demo 壳 |

## 已落地改造

1. 引入 `Microsoft.Extensions.AI` / `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Workflows`
2. `RefundAiToolCatalog`：`AIFunctionFactory` + `ApprovalRequiredAIFunction`
3. `ScenarioWorkflow`：`WorkflowBuilder` 动态边 + `InProcessExecution`
4. `RefundAgentHost`：`ChatClientAgent` + `UseFunctionInvocation`
5. `DeterministicRefundChatClient`：离线演示；可换 OpenAI / Ollama / Azure OpenAI
6. `HostingGuards`：非 Demo 强制 `ApiKey`；CI + Host Dockerfile

## 后续

- 加深 Http/Mcp 读路径与主数据源一致性（本仓内）
- 迁入官方 vue-vben-admin（可选）
- 扩展非退款垂直插件
