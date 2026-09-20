# StayOTA 业务系统对接协议（Agent 定义）

> **协议权威文件**：[`contracts/business-mcp-protocol.json`](../contracts/business-mcp-protocol.json)（`stayota.business.mcp` **v1.0.0**）  
> **本仓角色**：OTA Agent = **MCP Client**（编排 / 门禁 / HITL）  
> **业务系统角色**：StayOTA 订单/售后服务 = **MCP Server**（真相源）  
> Agent 自带的 `/mcp` 仅 Demo 桥接，**不是**正式业务 MCP。

---

## 1. 为什么由 Agent 定协议

Agent 需要稳定、可版本化的只读事实与写回执语义（金额、政策快照、订单 version、退款状态枚举等）。  
业务系统实现协议即可切换 `Production:Mode=Mcp`（或 `Http` 孪生），Agent 不静默回退 Mock。

```
StayOTA 业务 MCP Server                 stayota-agent (Client)
┌─────────────────────────┐            ┌──────────────────────────┐
│ stayota_get_order_detail│◄── MCP ───│ Chat / Workflow / Gates   │
│ stayota_list_user_orders│            │ ToolGateway + HITL       │
│ stayota_get_policy_*    │            │ Session / Case / Eval    │
│ stayota_get_refund_*    │            └──────────────────────────┘
└─────────────────────────┘
```

---

## 2. P0（必须先实现）：只读 MCP Tool

| MCP Tool 名（规范名） | 用途 | HTTP 孪生 |
| --- | --- | --- |
| `stayota_get_order_detail` | 权威订单快照 + `version` | `GET /orders/{orderId}?userId=` |
| `stayota_list_user_orders` | 用户可见订单列表 | `GET /users/{userId}/orders` |
| `stayota_get_policy_snapshot` | **成交时**政策快照 | `GET /policies/{policyId}?orderId=` |
| `stayota_get_refund_status` | 退款/渠道状态（勿把发起说成已到账） | `GET /refunds/{refundId}` |

参数、输出必填字段、状态枚举、兼容别名见 JSON 契约。错误码见 [`contracts/tool-errors.json`](../contracts/tool-errors.json)。

### 响应信封（MCP / HTTP 共用语义）

```json
{
  "ok": true,
  "code": "OK",
  "data": {},
  "source": "stayota-order-service",
  "occurred_at": "2026-09-20T06:00:00+08:00",
  "trace_id": "TR-EXAMPLE-001",
  "retryable": false
}
```

金额、政策、权限、状态字段为业务权威；Agent/LLM **不得改写**。

### 鉴权与追踪

- `Authorization: Bearer <JWT|ApiKey>`
- `X-Request-Id` 或 `X-Trace-Id`（与信封 `trace_id` 对齐）
- 可选：`X-Tenant-Id`、`X-User-Id`

---

## 3. P1（后续）：写操作承接

Agent 侧仍有完整 Tool 目录（见 [`contracts/tool-contracts.json`](../contracts/tool-contracts.json)）。  
写操作先过 Agent 门禁（确认令牌 / 订单 version / 幂等 / 审计），再调用业务写接口。P1 优先 HTTP，候选包括：

- `submit_cancellation` / `submit_order_change`
- `create_supplier_case` / `accept_supplier_offer`
- `create_finance_case`
- `get_action_result`（写结果未知时必须先查再重试）

业务侧需保证：幂等键、version 冲突码、审计回执 ID。

---

## 4. Agent 侧如何启用

```bash
export Production__Mode=Mcp
export Production__McpEndpoint='https://stayota.internal/mcp'
export Production__ApiKey='...'
# 或 HTTP 孪生：
# export Production__Mode=Http
# export Production__BaseUrl='https://orders.internal/'
```

规则：

1. `Mode=Mcp|Http` 时 **禁止** 静默回退 Mock  
2. 优先匹配规范 Tool 名；别名仅兼容  
3. 本仓 Demo `/mcp`（`stayota_*` / Echo）≠ 业务 MCP

---

## 5. 对接检查清单（业务）

- [ ] 实现 P0 四个 MCP Tool（名与参数对齐 JSON）  
- [ ] 返回信封 + `tool-errors.json` 错误码  
- [ ] 订单含 `user_id`、`version`、金额币种、政策/支付引用  
- [ ] `stayota_get_refund_status.status` 使用约定枚举  
- [ ] 提供 HTTP 孪生或明确仅 MCP  
- [ ] 联调：Agent `Production:Mode=Mcp` 打真实 endpoint，关闭 Demo 回退  

---

## 6. 版本策略

- 协议版本写在 JSON `version`（当前 **1.0.0**）  
- 可加字段；改名/删字段/改金额语义需升版本  
- Tool 改名时旧名至少兼容一个 Agent 发布周期（见 `aliases_accepted_by_agent`）
