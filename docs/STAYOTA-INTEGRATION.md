# StayOTA 模块融入方案（后端优先 · 前端后置）

> 前端不在本仓做嵌入式改造；正式 UI 后续按 StayOTA 后台风格重做。  
> 本文件只回答：**模块怎么并入、主站要提供什么支撑。**

---

## 1. 推荐形态：独立领域服务（OTA Agent）

```
┌─────────────────────────┐         ┌──────────────────────────────┐
│  StayOTA 主站            │         │  stayota-agent               │
│  · 登录/租户/菜单/权限    │  HTTP   │  · 通用运行时 + 垂直插件      │
│  · 订单/支付主数据        │◄──────►│  · FunctionApproval / HITL    │
│  · 后台 Vue（后续重做页）  │  MCP    │  · Case / Trace / Session     │
│  · API Gateway           │         │  · Eval（仅 Demo）            │
└─────────────────────────┘         └──────────────────────────────┘
```

**为什么不是「塞进主站一个 Class Library 就完事」：**

- OTA Agent 有独立会话、写门禁、长会话与审批流，和主站订单 CRUD 生命周期不同
- 可单独扩缩、单独发版、故障隔离；退款只是首个垂直插件，非边界
- 主站前端重做时，只换调用面，不必绑死进程内引用

**可选过渡：** 早期用 `AddStayOtaAgent()` + `AddAgentPlugin<RefundAgentPlugin>()` 挂进同一 Host（同仓联调）；接口与配置仍按「服务边界」设计，方便以后拆进程。

---

## 2. 模块边界（谁负责什么）

| 能力 | StayOTA 主站 | OTA Agent 模块 |
| --- | --- | --- |
| 身份 / SSO / 角色菜单 | ✅ | 校验 Token / ApiKey，不自建账号体系 |
| 订单、政策、支付真相源 | ✅ | 只读（`Production:Http\|Mcp`），写操作走受控 Tool |
| 垂直业务决策、风险、Tool（如退款） | | ✅ 插件贡献 |
| HITL / FunctionApproval | 主站页面触发 | ✅ 会话与审批状态 |
| Case / Trace / Eval | 可订阅事件 | ✅ 权威存储 |
| 后台页面（设计/处理台/看板） | ✅ **后续按主站风格重做** | 本仓 Vue 仅 Demo / 联调 |

---

## 3. 主站需要提供的支撑（清单）

### 3.1 身份与网关

- [ ] 统一鉴权：JWT（推荐）或 mTLS；短期可用 shared `ApiKey`
- [ ] Gateway 路由：`/ota-agent/**` → Agent 服务；透传 `Authorization` / 租户头
- [ ] 服务间调用身份：主站 server-to-server 调 Agent 时的 client credentials

### 3.2 订单 / 政策 / 支付只读 API（Production Http 契约）

Agent `Production:Mode=Http` 期望（可按主站现有 API 改名，但语义要对齐）：

| Agent 用途 | 建议主站接口 |
| --- | --- |
| 查单 | `GET /orders/{orderId}?userId=` |
| 用户订单列表 | `GET /users/{userId}/orders` |
| 成交政策快照 | `GET /policies/{policyId}?orderId=` |
| 退款进度 | `GET /refunds/{refundId}` |

要求：

- 稳定 JSON 契约 + 版本策略（字段可增不可无义变更）
- 超时 / 5xx 可重试语义明确；**禁止** Agent 静默回退 Mock
- 返回须含：订单归属 `userId`、`version`（乐观锁）、金额币种、入住状态等写门禁所需字段

### 3.3 写操作承接（二选一或组合）

1. **Agent 调主站写 API**（推荐）：`submit_cancellation` 等最终打到 StayOTA 售后写接口，Agent 只做编排与门禁  
2. **主站消费 Agent 决策事件**：Agent 产出「已批准写意图」，主站执行并回写结果  

无论哪种，主站需提供：幂等键、订单版本冲突码、审计回执 ID。

### 3.4 运行时与配置

- [ ] Postgres / Redis（可共用集群，**库或 key 前缀隔离**）
- [ ] 配置中心：`Production:Mode/BaseUrl`、`Ai:*`、`Hosting:ApiKey`
- [ ] 日志 / Trace：透传 `traceId` / `X-Request-Id`，接入主站可观测性
- [ ] 密钥：OpenAI / 内部 ApiKey 走 Secret，不进仓库

### 3.5 产品入口（前端后置时先定契约）

主站后台重做页之前，先约定入口参数（以后页面照此接）：

```
打开处理台：orderId, userId, tenantId?, caseId?
审批回调：agentSessionId, requestId, approved
```

本仓 Demo 页可继续用 A–L 场景；正式页以「订单上下文」进入，而不是 Demo 场景列表。

### 3.6 MCP（可选）

- 主站或外部 Agent 可连本服务 `/mcp` 调 `stayota_*` 只读 Tool  
- 需与 HTTP 同一套鉴权；生产勿对公网裸奔

---

## 4. 本模块对外契约（主站对接面）

稳定 API（正式保留）：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| POST | `/api/agent/message` | 一轮对话 / 决策 |
| POST | `/api/agent/approvals` | FunctionApproval 续跑 |
| GET | `/api/scenarios` | 场景元数据（Demo/配置） |
| GET | `/api/tools` | Tool 契约列表 |
| GET | `/api/hosting` | demo/auth 开关 |
| GET | `/health` `/health/live` `/health/ready` | 探针 |
| * | `/mcp` | MCP |

Demo-only（正式 `DemoEnabled=false` 关闭）：

- `POST /api/eval/*`、`POST /api/workflows/*`、开放 `POST /api/confirmations`、启动删库、`ResetDemo`

类库挂载点（过渡）：

```csharp
services.AddStayOtaAgent(configuration);   // Runtime：Redis / ChatClient / Conversation
services.AddAgentPlugin<RefundAgentPlugin>(configuration); // 退款垂直演示包（可换其他垂直）
// PathBase 可选：Hosting:PathBase=/ota-agent
// Schema 隔离：AgentStorage:Schema=agent（默认，与垂直业务解耦）
```

模块布局（对齐 StayOTA Scheduling）：

```
src/Modules/Agent/
  StayOta.Agent.Abstractions   # 契约 / Options / Domain
  StayOta.Agent                # Runtime（AddStayOtaAgent）
  StayOta.Agent.Plugins.Refund # 退款垂直演示包（AddAgentPlugin）
src/Hosts/StayOta.Agent.Host   # 独立 Host
```

---

## 5. 落地顺序（建议）

1. **主站提供**只读订单/政策 API + 鉴权 + 网关路由  
2. **Agent** `Production:Mode=Http` 联调真实读路径；关闭 Mock 回退（已做）  
3. **写路径**对齐：取消/改期等主站售后 API + 幂等/版本  
4. **关闭 Demo**（`Hosting:DemoEnabled=false`），独立部署 Agent  
5. **前端**按 StayOTA 后台风格重做三页，只消费上述 API（本仓 Vue 退役或仅作联调）

---

## 6. 明确不做（本阶段）

- 不在本仓把 Vue 改成「嵌入主站 Layout」的微前端结构  
- 不要求主站现在就合并代码进同一前端工程  
- 不把 33 Tool 实现搬进主站；主站只对接 API / 写回执
