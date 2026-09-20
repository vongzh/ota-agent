import type { Page, Route } from '@playwright/test'

const scenarios = [
  {
    scenarioId: 'G',
    title: '取消退款（标准）',
    group: '取消',
    goal: '按政策计算退款',
    entryMessage: '我想取消订单并申请退款',
    riskLevel: 'Low',
    expectedRoute: 'auto',
    requiredTools: ['get_order', 'get_policy'],
  },
  {
    scenarioId: 'A',
    title: '到账查询',
    group: '到账',
    goal: '查询退款进度',
    entryMessage: '退款什么时候到账',
    riskLevel: 'Low',
    expectedRoute: 'auto',
    requiredTools: ['get_refund_status'],
  },
]

const decision = {
  traceId: 'tr-e2e-1',
  runId: 'run-e2e-1',
  caseId: 'case-e2e-1',
  scenarioId: 'G',
  intent: 'cancel_refund',
  intentConfidence: 0.92,
  riskLevel: 'Low',
  riskScore: 0.1,
  action: 'refund',
  conclusion: '可按政策全额退款',
  planTitle: '标准取消退款',
  planCopy: '确认后执行退款写操作',
  refundAmount: 688,
  feeAmount: 0,
  reply: '已根据政策计算：可退款 ¥688。确认后我将提交退款。',
  conversationState: 'DECISION_READY',
  caseStatus: 'Open',
  steps: [
    { step: '意图识别', status: 'success', detail: 'cancel_refund' },
    { step: '槽位提取', status: 'success', detail: 'orderId=ORD-E2E' },
    { step: '订单查询', status: 'success', detail: 'ok' },
    { step: '政策检索', status: 'success', detail: 'flex' },
    { step: '规则校验', status: 'success', detail: 'pass' },
    { step: '风险判断', status: 'success', detail: 'Low' },
    { step: '处理动作', status: 'success', detail: 'refund' },
  ],
  slots: { orderId: 'ORD-E2E' },
  policyMatches: [{ policyId: 'P1', title: '灵活取消', score: 0.9, summary: '可退' }],
  toolSequence: ['get_order', 'get_policy'],
  order: {
    orderId: 'ORD-E2E',
    hotelName: 'E2E 演示酒店',
    checkIn: '2026-10-01',
    checkOut: '2026-10-03',
    amount: 688,
    currency: 'CNY',
    status: 'Confirmed',
    userOnSite: false,
    policyId: 'P1',
    version: 1,
    roomType: '标准间',
    roomCount: 1,
  },
  verificationPassed: true,
  verificationViolations: [],
  aiProvider: 'Deterministic',
  agentSessionId: 'sess-e2e-1',
  agentDriven: true,
  hasPendingApprovals: false,
  pendingApprovals: [],
  productionMode: 'Mock',
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}

function sse(route: Route, events: Array<{ event?: string; data: unknown }>) {
  const body = events
    .map((e) => {
      const lines: string[] = []
      if (e.event) lines.push(`event: ${e.event}`)
      lines.push(`data: ${JSON.stringify(e.data)}`)
      return lines.join('\n')
    })
    .join('\n\n')
  return route.fulfill({
    status: 200,
    contentType: 'text/event-stream',
    headers: { 'Cache-Control': 'no-cache' },
    body: `${body}\n\n`,
  })
}

/**
 * Deterministic API stubs so CI needs no Postgres/Redis/Host.
 * Register catch-all first; Playwright matches last-registered first.
 */
export async function installMockApi(page: Page) {
  await page.route('**/api/**', (route) => {
    if (route.request().method() === 'GET') return json(route, [])
    return json(route, { ok: true })
  })

  await page.route('**/health', (route) =>
    json(route, {
      status: 'ok',
      scenarios: scenarios.length,
      tools: 33,
      aiProvider: 'Deterministic',
      agent: 'StayOTA',
      demoEnabled: true,
      productionMode: 'Mock',
      authRequired: false,
    }),
  )

  await page.route('**/api/hosting', (route) =>
    json(route, { demoEnabled: true, authRequired: false }),
  )

  await page.route('**/api/scenarios', (route) => json(route, scenarios))

  await page.route('**/api/plugins', (route) =>
    json(route, [
      { id: 'refund', displayName: 'Refund', agentName: 'RefundAgent', isPrimary: true },
      { id: 'echo', displayName: 'Echo', agentName: 'EchoAgent', isPrimary: false },
    ]),
  )

  await page.route('**/api/ops/summary', (route) =>
    json(route, {
      generatedAt: new Date().toISOString(),
      source: 'agent-process',
      northStar: {
        label: 'Tool 门禁放行率（Agent 过程指标）',
        value: '95%',
        note: '基于近 20 条 Tool 审计；非业务退款闭环率',
        isProcessMetric: true,
      },
      businessNorthStar: {
        label: '正确退款任务闭环率',
        status: 'pending',
        note: '业务侧待接入（进线量、真实退款到账等不在本仓聚合）',
      },
      metrics: [
        { group: '会话', name: '活跃 Session', value: '2', note: '来自 IAgentSessionStore', tone: 'ok' },
        { group: '审批', name: '待审批请求', value: '0', note: 'HITL / FunctionApproval 挂起数', tone: 'ok' },
        { group: '风险', name: 'Tool 拦截率', value: '5%', note: '1 次拒绝 / 20 次调用', tone: 'warn' },
        { group: '能力', name: 'Eval 用例', value: '36', note: '离线评测目录规模（本仓可跑）', tone: 'ok' },
        { group: '编排', name: 'Workflow 成功率', value: '100%', note: '12/12 成功', tone: 'ok' },
        { group: '插件', name: '已加载插件', value: '2', note: 'refund、echo', tone: 'ok' },
      ],
      funnel: [
        { stage: 'Agent Session', in: 2, drop: 0, note: '会话快照' },
        { stage: 'Tool 审计', in: 20, drop: 0, note: '含放行与拦截' },
        { stage: '门禁放行', in: 19, drop: 1, note: 'Allowed=true' },
        { stage: 'Workflow 落库', in: 12, drop: 7, note: '编排运行记录' },
        { stage: 'Case 更新', in: 8, drop: 4, note: 'RefundCase 状态' },
      ],
      riskItems: [
        { type: 'write requires approval', count: 1, risk: '高', stage: '发现', owner: 'submit_cancellation' },
      ],
      sessions: { total: 2, withPendingApproval: 0, pendingApprovalTotal: 0 },
      audits: { total: 20, allowed: 19, denied: 1, denyRate: 0.05 },
      workflows: { total: 12, succeeded: 12, failed: 0, running: 0 },
      cases: { total: 8, byStatus: { REFUND_INITIATED: 5, OPEN: 3 } },
      eval: { caseCount: 36 },
      plugins: {
        count: 2,
        items: [
          { id: 'refund', displayName: 'Refund', isPrimary: true },
          { id: 'echo', displayName: 'Echo', isPrimary: false },
        ],
      },
    }),
  )

  await page.route('**/api/mcp/tools', (route) =>
    json(route, [{ name: 'echo_ping', source: 'echo', access: 'read', purpose: 'ping' }]),
  )

  await page.route('**/api/ai/provider', (route) => {
    if (route.request().method() === 'GET') {
      return json(route, {
        configuredProvider: 'Deterministic',
        effectiveProvider: 'Deterministic',
        effectiveModel: '',
        allowed: ['Deterministic', 'OpenAI', 'Ollama'],
      })
    }
    return json(route, { ok: true })
  })

  await page.route('**/api/agent/sessions**', (route) => {
    if (route.request().method() === 'GET') return json(route, [])
    return json(route, { ok: true })
  })

  await page.route('**/api/agent/message/stream', (route) =>
    sse(route, [
      { event: 'step', data: { type: 'step', text: '意图识别' } },
      { event: 'tool', data: { type: 'tool', text: 'get_order' } },
      {
        event: 'reply_delta',
        data: { type: 'reply_delta', text: '已根据政策计算：可退款 ¥688。' },
      },
      { event: 'done', data: { type: 'done', data: decision } },
    ]),
  )

  await page.route('**/api/agent/message', (route) => json(route, decision))
}
