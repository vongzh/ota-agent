<template>
  <div class="workspace">
    <div class="workspace-heading">
      <div>
        <h1>智能处理台</h1>
        <p>结论、金额与权限来自规则与 Tool；模型侧自主选 Tool，同会话可多轮续跑。</p>
      </div>
      <button class="ghost-btn" :disabled="loading" @click="resetAndRun">重置当前场景</button>
    </div>

    <ol class="workflow-strip" aria-label="Agent 处理流程">
      <li
        v-for="(step, idx) in pipeline"
        :key="step.step"
        class="workflow-step"
        :class="stepClass(step.status)"
      >
        <span class="stage-index">{{ idx + 1 }}</span>
        <span class="stage-label">{{ step.step }}</span>
        <span class="stage-state">{{ statusLabel(step.status) }}</span>
      </li>
    </ol>

    <div class="mobile-switch">
      <button :class="{ active: mobilePane === 'chat' }" @click="mobilePane = 'chat'">对话</button>
      <button :class="{ active: mobilePane === 'decision' }" @click="mobilePane = 'decision'">决策</button>
    </div>

    <div class="workspace-grid">
      <aside class="scenario-rail">
        <div class="rail-heading">
          <h2>演示场景</h2>
          <p>A–L 覆盖取消、到账、履约、协商、凭证、争议、改期、财务、跨境、团体</p>
        </div>
        <div class="scenario-list">
          <button
            v-for="item in scenarios"
            :key="item.scenarioId"
            class="scenario-item"
            :class="{ active: item.scenarioId === activeId }"
            @click="selectScenario(item.scenarioId)"
          >
            <span class="scenario-id">{{ item.scenarioId }}</span>
            <span class="scenario-body">
              <strong>{{ item.title }}</strong>
              <small>{{ item.group }} · {{ item.riskLevel }}</small>
            </span>
          </button>
        </div>
        <div class="boundary-block">
          <h3>边界态</h3>
          <button class="ghost-btn block" @click="runBoundary({ lowConfidence: true })">低置信度澄清</button>
          <button class="ghost-btn block danger" @click="runBoundary({ serviceError: true })">订单服务异常</button>
        </div>
      </aside>

      <section class="panel conversation-panel" :class="{ 'mobile-hidden': mobilePane !== 'chat' }">
        <div class="panel-header">
          <div>
            <span class="panel-icon">💬</span>
            <div>
              <strong>对话工作台</strong>
              <p>{{ decision?.caseId || '等待选择场景' }} · {{ decision?.conversationState || 'IDLE' }}</p>
            </div>
          </div>
          <span class="status-pill" :class="riskTone(decision?.riskLevel)">{{ decision?.riskLevel || '—' }}</span>
        </div>

        <div class="order-ribbon" v-if="decision">
          <span>{{ decision.order.hotelName }} · {{ decision.order.orderId }}</span>
          <span>{{ decision.order.checkIn }} → {{ decision.order.checkOut }}</span>
          <span>{{ decision.order.currency }} {{ decision.order.amount }}</span>
          <span>{{ decision.caseStatus }}</span>
        </div>
        <div class="order-ribbon muted" v-else>
          <span>选择左侧场景后，将展示订单事实条与 Agent 回复</span>
        </div>

        <div class="conversation-body">
          <div class="message-stream">
            <div class="message">
              <div class="message-avatar agent-avatar">AI</div>
              <div class="bubble">
                我是酒店售后 Agent。金额、权限与写操作由规则引擎与 Tool 门禁决定，不会由模型自由生成。
              </div>
            </div>
            <div class="message message-user" v-if="userMessage">
              <div class="bubble user">{{ userMessage }}</div>
              <div class="message-avatar user-avatar">我</div>
            </div>
            <div class="message" v-if="loading">
              <div class="message-avatar agent-avatar">AI</div>
              <div class="bubble thinking">
                <template v-if="streamStatus">{{ streamStatus }}</template>
                <template v-else>正在走意图 → 订单 → 政策 → 规则 → 风险 → 动作…</template>
                <p v-if="streamReply" class="stream-reply">{{ streamReply }}</p>
              </div>
            </div>
            <div class="message" v-if="decision && !loading">
              <div class="message-avatar agent-avatar">AI</div>
              <div class="bubble">
                <p>{{ decision.reply }}</p>
                <div class="solution-card" v-if="decision.planTitle">
                  <strong>{{ decision.planTitle }}</strong>
                  <p>{{ decision.planCopy || decision.conclusion }}</p>
                  <div class="money" v-if="decision.refundAmount != null">
                    <span>预计退回 <b>{{ decision.order.currency }} {{ decision.refundAmount }}</b></span>
                    <span v-if="decision.feeAmount != null">费用 {{ decision.feeAmount }}</span>
                  </div>
                </div>
              </div>
            </div>
            <div class="message" v-if="errorText">
              <div class="message-avatar agent-avatar danger">!</div>
              <div class="bubble error">{{ errorText }}</div>
            </div>
          </div>
        </div>

        <div class="action-result" v-if="decision && !loading">
          <div class="action-result-heading">
            <strong>{{ actionLabel(decision.action) }}</strong>
            <span class="status-pill" :class="decision.verificationPassed ? 'success' : 'danger'">
              Verifier {{ decision.verificationPassed ? '通过' : '未通过' }}
            </span>
          </div>
          <div v-if="decision.hitl?.requiresConfirmation || decision.hasPendingApprovals" class="hitl-banner">
            <div>
              <strong>{{ decision.hasPendingApprovals ? '官方 FunctionApproval 待批' : 'HITL 写门禁待确认' }}</strong>
              <p v-if="decision.hasPendingApprovals && decision.pendingApprovals?.length">
                Tool <code>{{ decision.pendingApprovals[0].toolName }}</code>
                · {{ decision.pendingApprovals[0].description }}
              </p>
              <p v-else>
                动作 <code>{{ decision.hitl?.pendingAction }}</code>
                · {{ decision.hitl?.gate }}
              </p>
            </div>
            <span class="status-pill warning">{{ decision.hasPendingApprovals ? 'ToolApprovalRequest' : 'ApprovalRequired' }}</span>
          </div>
          <div class="action-ctas">
            <button
              v-if="decision.action === 'RequestEvidence'"
              class="primary-btn"
              :disabled="loading"
              @click="withEvidence"
            >上传示例凭证</button>
            <button
              v-if="decision.action === 'RequestInformation'"
              class="primary-btn"
              :disabled="loading"
              @click="withReason"
            >补充无法入住原因</button>
            <template v-if="decision.hasPendingApprovals && decision.pendingApprovals?.length && decision.agentSessionId">
              <button class="primary-btn" :disabled="loading" @click="approveFunction(true)">批准执行</button>
              <button class="ghost-btn" :disabled="loading" @click="approveFunction(false)">拒绝</button>
            </template>
            <button
              v-else-if="decision.action === 'ConfirmCancel' || decision.action === 'ChangeOrder' || decision.hitl?.requiresConfirmation"
              class="primary-btn"
              :disabled="loading"
              @click="confirmWrite"
            >确认执行写操作</button>
            <span class="status-pill info">{{ decision.conversationState }}</span>
            <span v-if="decision.aiProvider" class="status-pill neutral">AI · {{ decision.aiProvider }}</span>
            <span v-if="decision.agentDriven" class="status-pill success">Agent 驱动</span>
            <span v-if="decision.productionMode" class="status-pill neutral">Prod · {{ decision.productionMode }}</span>
          </div>
        </div>

        <div class="composer">
          <input v-model="draft" placeholder="输入诉求，或点左侧场景一键演示" @keydown.enter="runCustom" />
          <button class="primary-btn" :disabled="loading" @click="runCustom">发送</button>
        </div>
      </section>

      <aside class="panel decision-panel" :class="{ 'mobile-hidden': mobilePane !== 'decision' }">
        <div class="panel-header">
          <div>
            <span class="panel-icon">◎</span>
            <div>
              <strong id="decision-title">Agent 决策</strong>
              <p>意图 · 风险 · 动作 · 轨迹 · 政策 · Tool</p>
            </div>
          </div>
          <span class="status-pill" :class="riskTone(decision?.riskLevel)">
            {{ decision ? `${decision.riskScore}/100` : '待机' }}
          </span>
        </div>

        <div class="decision-scroll">
          <template v-if="decision">
            <div class="kpi-row">
              <div>
                <span>意图</span>
                <strong>{{ Math.round(decision.intentConfidence * 100) }}%</strong>
                <small>{{ decision.intent }}</small>
              </div>
              <div>
                <span>风险</span>
                <strong>{{ decision.riskLevel }}</strong>
                <small>{{ decision.riskScore }} 分</small>
              </div>
              <div>
                <span>动作</span>
                <strong>{{ actionLabel(decision.action) }}</strong>
                <small>{{ decision.caseStatus }}</small>
              </div>
            </div>

            <div class="decision-section" v-if="!decision.verificationPassed">
              <h3>Verifier 违规</h3>
              <ul class="violation-list">
                <li v-for="v in decision.verificationViolations" :key="v">{{ v }}</li>
              </ul>
            </div>

            <details class="decision-section" open>
              <summary>决策轨迹</summary>
              <ol class="trace">
                <li v-for="step in decision.steps" :key="step.step">
                  <span class="status-pill" :class="pillFromStatus(step.status)">{{ statusLabel(step.status) }}</span>
                  <div>
                    <strong>{{ step.step }}</strong>
                    <p>{{ step.detail }}</p>
                  </div>
                </li>
              </ol>
            </details>

            <details class="decision-section" open>
              <summary>槽位</summary>
              <dl class="slot-grid">
                <template v-for="(v, k) in decision.slots" :key="k">
                  <dt>{{ k }}</dt>
                  <dd>{{ v }}</dd>
                </template>
              </dl>
            </details>

            <details class="decision-section" open>
              <summary>政策 Top3</summary>
              <div v-for="p in decision.policyMatches" :key="p.policyId" class="policy-row">
                <div class="policy-meta">
                  <strong>{{ p.policyId }}</strong>
                  <span>{{ p.title }}</span>
                </div>
                <div class="score-bar"><i :style="{ width: `${Math.round(p.score * 100)}%` }" /></div>
                <small>{{ p.summary }}</small>
              </div>
            </details>

            <details class="decision-section" open>
              <summary>Tool 序列</summary>
              <div class="tool-chips">
                <span v-for="t in decision.toolSequence" :key="t">{{ t }}</span>
              </div>
            </details>

            <details class="decision-section" v-if="decision.ticket" open>
              <summary>工单 · {{ decision.ticket.ticketId }}</summary>
              <p class="ticket-line">{{ decision.ticket.priority }} / {{ decision.ticket.queue }} · {{ decision.ticket.summary }}</p>
              <ul>
                <li v-for="f in decision.ticket.facts" :key="f">{{ f }}</li>
              </ul>
              <div v-if="decision.ticket.lifecycle?.length" class="lifecycle">
                <div
                  v-for="step in decision.ticket.lifecycle"
                  :key="step.stage"
                  class="lifecycle-step"
                  :data-status="step.status"
                >
                  <span class="dot" />
                  <div>
                    <strong>{{ step.stage }}</strong>
                    <p>{{ step.detail }}</p>
                  </div>
                  <em>{{ step.status }}</em>
                </div>
              </div>
            </details>
          </template>
          <div v-else-if="loading" class="skeleton-decision" aria-busy="true">
            <div class="sk-row" /><div class="sk-row" /><div class="sk-row short" />
            <p>决策面板加载中…</p>
          </div>
          <div v-else class="empty-decision">
            <strong>决策面板待机</strong>
            <p>跑通场景后，这里会展示风险分层、轨迹、政策分数条、Tool 调用序与工单生命周期。</p>
          </div>
        </div>
      </aside>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import { fetchHosting, fetchScenarios, runAgentMessageStream, respondToApproval } from '@/api/agent'
import type { AgentDecision, Scenario } from '@/types'

const scenarios = ref<Scenario[]>([])
const activeId = ref('G')
const decision = ref<AgentDecision | null>(null)
const userMessage = ref('')
const draft = ref('')
const loading = ref(false)
const errorText = ref('')
const mobilePane = ref<'chat' | 'decision'>('chat')
const demoEnabled = ref(true)
const streamStatus = ref('')
const streamReply = ref('')

const pipeline = computed(() => decision.value?.steps ?? [
  { step: '意图识别', status: 'pending', detail: '' },
  { step: '槽位提取', status: 'pending', detail: '' },
  { step: '订单查询', status: 'pending', detail: '' },
  { step: '政策检索', status: 'pending', detail: '' },
  { step: '规则校验', status: 'pending', detail: '' },
  { step: '风险判断', status: 'pending', detail: '' },
  { step: '处理动作', status: 'pending', detail: '' },
])

onMounted(async () => {
  try {
    try {
      const hosting = await fetchHosting()
      demoEnabled.value = hosting.demoEnabled
    } catch {
      demoEnabled.value = true
    }
    scenarios.value = await fetchScenarios()
    await selectScenario(activeId.value)
  } catch {
    message.error('无法连接后端 API')
  }
})

async function selectScenario(id: string) {
  activeId.value = id
  const scenario = scenarios.value.find((s) => s.scenarioId === id)
  if (!scenario) return
  await run({
    message: scenario.entryMessage,
    scenarioId: id,
    ...(demoEnabled.value ? { resetDemo: true } : {}),
  })
}
async function withEvidence() {
  await run({ message: userMessage.value || '已上传证明', scenarioId: activeId.value, hasEvidence: true })
}
async function withReason() {
  await run({
    message: '因家人临时住院无法入住，请协助与酒店沟通。',
    scenarioId: activeId.value,
    hasNegotiationReason: true,
  })
}
async function confirmWrite() {
  const d = decision.value
  // Prefer unified FunctionApproval path when pending approvals already exist.
  if (d?.hasPendingApprovals && d.pendingApprovals?.length && d.agentSessionId) {
    await approveFunction(true)
    return
  }
  await run({
    message: userMessage.value,
    scenarioId: activeId.value,
    confirmWrite: true,
    idempotencyKey: `ui-${activeId.value}-${Date.now()}`,
  })
}
async function approveFunction(approved: boolean) {
  const d = decision.value
  const pending = d?.pendingApprovals?.[0]
  if (!d?.agentSessionId || !pending) return
  loading.value = true
  errorText.value = ''
  try {
    decision.value = await respondToApproval({
      sessionId: d.agentSessionId,
      requestId: pending.requestId,
      approved,
      reason: approved ? '用户批准写操作' : '用户拒绝写操作',
    })
  } catch (e: unknown) {
    const err = e as { response?: { data?: { message?: string } } }
    errorText.value = err.response?.data?.message || 'FunctionApproval 失败'
  } finally {
    loading.value = false
  }
}
async function runBoundary(flags: Record<string, boolean>) {
  await run({
    message: flags.lowConfidence
      ? '我想问一下这个事情怎么处理'
      : userMessage.value || scenarios.value.find((s) => s.scenarioId === activeId.value)?.entryMessage,
    scenarioId: activeId.value,
    ...flags,
  })
}
async function runCustom() {
  if (!draft.value.trim()) return
  await run({ message: draft.value, scenarioId: activeId.value })
  draft.value = ''
}
async function resetAndRun() {
  await selectScenario(activeId.value)
}

async function run(payload: Record<string, unknown>) {
  loading.value = true
  errorText.value = ''
  streamStatus.value = '开始处理…'
  streamReply.value = ''
  userMessage.value = String(payload.message || '')
  try {
    const resumeSession =
      !payload.resetDemo &&
      decision.value?.agentSessionId &&
      payload.scenarioId === activeId.value
    if (resumeSession) {
      payload = { ...payload, agentSessionId: decision.value!.agentSessionId }
    }
    decision.value = await runAgentMessageStream(payload, {
      onStep: (text) => { streamStatus.value = `步骤：${text}` },
      onTool: (name) => { streamStatus.value = `Tool：${name}` },
      onReplyDelta: (chunk) => { streamReply.value += chunk },
      onError: (msg) => { errorText.value = msg },
    })
  } catch (e: unknown) {
    const err = e as { response?: { data?: { message?: string } }; message?: string }
    errorText.value = err.response?.data?.message || err.message || 'Agent 调用失败'
    decision.value = null
  } finally {
    loading.value = false
    streamStatus.value = ''
    streamReply.value = ''
  }
}

function statusLabel(status: string) {
  return ({ success: '成功', warning: '需关注', error: '失败', active: '进行中', pending: '待执行' } as Record<string, string>)[status] || status
}
function stepClass(status: string) {
  if (status === 'success') return 'is-complete'
  if (status === 'warning') return 'is-warning'
  if (status === 'active') return 'is-active'
  if (status === 'error') return 'is-warning'
  return ''
}
function pillFromStatus(status: string) {
  if (status === 'success') return 'success'
  if (status === 'warning') return 'warning'
  if (status === 'active') return 'info'
  if (status === 'error') return 'danger'
  return 'neutral'
}
function riskTone(level?: string) {
  if (level === 'L3') return 'danger'
  if (level === 'L2') return 'warning'
  if (level === 'L1') return 'success'
  return 'neutral'
}
function actionLabel(action: string) {
  return ({
    WriteApproved: '写操作已批准', WriteRejected: '写操作已拒绝',
    ConfirmCancel: '确认取消', RequestEvidence: '补充材料', RequestInformation: '补充信息',
    NegotiateWithHotel: '酒店协商', HumanHandoff: '转人工', ExplainProgress: '同步进度',
    Clarify: '澄清', ChangeOrder: '改期', FinanceReview: '财务核验', SpecialReview: '特殊审核',
    ServiceDispute: '服务争议', Recovery: '履约恢复', AutoRefund: '自动退款',
  } as Record<string, string>)[action] || action
}
</script>

<style scoped>
.workspace-heading {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}
.workspace-heading h1 {
  margin: 0 0 0.2rem;
  font-size: 1.55rem;
  letter-spacing: -0.02em;
}
.workspace-heading p {
  margin: 0;
  color: var(--color-ink-muted);
  font-size: var(--font-sm);
}

.ghost-btn {
  min-height: 36px;
  padding: 0 0.9rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  color: var(--color-ink);
  cursor: pointer;
}
.ghost-btn:hover { border-color: var(--color-border-strong); }
.ghost-btn.block { width: 100%; margin-top: 0.4rem; text-align: left; }
.ghost-btn.danger { color: var(--color-danger); border-color: oklch(0.82 0.06 28); background: var(--color-danger-soft); }
.primary-btn {
  min-height: 36px;
  padding: 0 0.95rem;
  border: 0;
  border-radius: var(--radius-md);
  background: var(--color-accent-strong);
  color: #fff;
  cursor: pointer;
  font-weight: 600;
}
.primary-btn:disabled, .ghost-btn:disabled { opacity: 0.55; cursor: not-allowed; }

.workflow-strip {
  list-style: none;
  margin: 0 0 1rem;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  overflow: hidden;
  box-shadow: var(--shadow-panel);
}
.workflow-step {
  min-width: 0;
  display: grid;
  grid-template-columns: auto 1fr;
  grid-template-rows: auto auto;
  gap: 0.15rem 0.45rem;
  align-items: center;
  padding: 0.65rem 0.75rem;
  color: var(--color-ink-muted);
  border-right: 1px solid var(--color-border);
  font-size: 11px;
}
.workflow-step:last-child { border-right: 0; }
.workflow-step.is-active {
  color: var(--color-accent-strong);
  background: var(--color-accent-soft);
  font-weight: 650;
}
.workflow-step.is-complete { color: var(--color-success); }
.workflow-step.is-warning { color: var(--color-warning); background: var(--color-warning-soft); }
.stage-index {
  width: 20px; height: 20px;
  display: grid; place-items: center;
  border: 1px solid currentColor;
  border-radius: 999px;
  font-size: 10px;
  grid-row: span 2;
}
.stage-label { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.stage-state { grid-column: 2; opacity: 0.85; }

.workspace-grid {
  display: grid;
  grid-template-columns: 220px minmax(0, 1.15fr) minmax(0, 0.95fr);
  gap: 1rem;
  align-items: start;
}

.rail-heading { padding: 0.15rem 0.25rem 0.75rem; }
.rail-heading h2 { margin: 0 0 0.25rem; font-size: 1rem; }
.rail-heading p { margin: 0; color: var(--color-ink-muted); font-size: 11px; }
.scenario-list { display: grid; gap: 0.4rem; }
.scenario-item {
  width: 100%;
  display: grid;
  grid-template-columns: 26px 1fr;
  gap: 0.5rem;
  padding: 0.7rem 0.65rem;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  background: transparent;
  text-align: left;
  cursor: pointer;
  color: inherit;
}
.scenario-item:hover { background: var(--color-surface); border-color: var(--color-border); }
.scenario-item.active {
  background: var(--color-surface);
  border-color: oklch(0.82 0.055 225);
  box-shadow: var(--shadow-panel);
}
.scenario-id {
  width: 26px; height: 26px;
  display: grid; place-items: center;
  border-radius: var(--radius-md);
  background: var(--color-surface-muted);
  font-size: 11px; font-weight: 700;
}
.scenario-item.active .scenario-id {
  background: var(--color-accent-soft);
  color: var(--color-accent-strong);
}
.scenario-body { display: grid; gap: 0.15rem; min-width: 0; }
.scenario-body strong {
  font-size: var(--font-sm);
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.scenario-body small { color: var(--color-ink-muted); font-size: 11px; }
.boundary-block { margin-top: 1rem; padding-top: 0.75rem; border-top: 1px solid var(--color-border); }
.boundary-block h3 { margin: 0 0 0.35rem; font-size: var(--font-sm); }

.conversation-panel, .decision-panel {
  height: clamp(560px, calc(100vh - 220px), 760px);
  display: grid;
  overflow: hidden;
}
.conversation-panel { grid-template-rows: auto auto minmax(0, 1fr) auto auto; }
.decision-panel { grid-template-rows: auto minmax(0, 1fr); }

.panel-header {
  min-height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0 1rem;
  border-bottom: 1px solid var(--color-border);
}
.panel-header > div { display: flex; align-items: center; gap: 0.7rem; min-width: 0; }
.panel-header strong { display: block; font-size: var(--font-sm); }
.panel-header p { margin: 0; color: var(--color-ink-muted); font-size: 11px; }
.panel-icon {
  width: 32px; height: 32px;
  display: grid; place-items: center;
  border-radius: var(--radius-md);
  background: var(--color-accent-soft);
  color: var(--color-accent-strong);
  font-size: 13px;
}

.order-ribbon {
  min-height: 42px;
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0 1rem;
  background: var(--color-surface-muted);
  border-bottom: 1px solid var(--color-border);
  color: var(--color-ink-muted);
  font-size: 11px;
  overflow: auto;
}
.order-ribbon > span:first-child { color: var(--color-ink); font-weight: 600; }
.order-ribbon.muted { color: var(--color-ink-muted); }

.conversation-body, .decision-scroll {
  min-height: 0;
  overflow: auto;
}
.conversation-body { padding: 1.1rem; }
.message-stream { display: grid; gap: 1rem; }
.message { display: flex; gap: 0.65rem; align-items: flex-start; }
.message-user { justify-content: flex-end; }
.message-avatar {
  width: 30px; height: 30px; flex: 0 0 30px;
  display: grid; place-items: center;
  border-radius: var(--radius-md);
  font-size: 11px; font-weight: 700;
}
.agent-avatar { background: var(--color-accent-strong); color: #fff; }
.agent-avatar.danger { background: var(--color-danger); }
.user-avatar { background: var(--color-surface-strong); color: var(--color-ink); }
.bubble {
  max-width: 88%;
  padding: 0.75rem 0.85rem;
  border-radius: 10px;
  background: var(--color-surface-muted);
  border: 1px solid var(--color-border);
  font-size: var(--font-sm);
}
.bubble p { margin: 0 0 0.55rem; }
.bubble.user {
  background: var(--color-accent-strong);
  color: #fff;
  border-color: transparent;
}
.bubble.thinking { color: var(--color-ink-muted); font-style: italic; }
.stream-reply {
  margin-top: 0.55rem;
  font-style: normal;
  color: var(--color-ink);
  white-space: pre-wrap;
}
.bubble.error { background: var(--color-danger-soft); color: var(--color-danger); border-color: oklch(0.82 0.06 28); }

.solution-card {
  margin-top: 0.35rem;
  padding: 0.7rem 0.75rem;
  border-radius: var(--radius-md);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
}
.solution-card strong { display: block; margin-bottom: 0.25rem; }
.solution-card p { margin: 0 0 0.45rem; color: var(--color-ink-muted); }
.money { display: flex; gap: 0.85rem; flex-wrap: wrap; font-size: 12px; }
.money b { color: var(--color-accent-strong); }

.action-result {
  padding: 0.75rem 1rem;
  border-top: 1px solid var(--color-border);
  background: oklch(0.975 0.01 225);
}
.action-result-heading {
  display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; margin-bottom: 0.5rem;
}
.action-ctas { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }

.composer {
  display: flex; gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-top: 1px solid var(--color-border);
}
.composer input {
  flex: 1;
  min-height: 36px;
  padding: 0 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
}

.decision-scroll { padding: 0.85rem 1rem 1.25rem; }
.kpi-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.5rem;
  margin-bottom: 0.85rem;
}
.kpi-row > div {
  padding: 0.65rem 0.7rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface-muted);
  display: grid; gap: 0.15rem;
}
.kpi-row span { font-size: 10px; color: var(--color-ink-muted); text-transform: uppercase; letter-spacing: 0.04em; }
.kpi-row strong { font-size: var(--font-sm); }
.kpi-row small { color: var(--color-ink-muted); font-size: 11px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.decision-section {
  border-top: 1px solid var(--color-border);
  padding: 0.7rem 0 0.15rem;
  margin-top: 0.35rem;
}
.decision-section summary {
  cursor: pointer;
  font-size: var(--font-sm);
  font-weight: 650;
  margin-bottom: 0.55rem;
}
.trace { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.55rem; }
.trace li { display: grid; grid-template-columns: auto 1fr; gap: 0.55rem; align-items: start; }
.trace p { margin: 0.15rem 0 0; color: var(--color-ink-muted); font-size: 12px; }
.slot-grid {
  display: grid;
  grid-template-columns: 110px 1fr;
  gap: 0.35rem 0.6rem;
  margin: 0;
  font-size: 12px;
}
.slot-grid dt { color: var(--color-ink-muted); }
.slot-grid dd { margin: 0; }
.policy-row { margin-bottom: 0.7rem; }
.policy-meta { display: flex; justify-content: space-between; gap: 0.5rem; font-size: 12px; margin-bottom: 0.3rem; }
.score-bar {
  height: 6px;
  border-radius: 999px;
  background: var(--color-surface-muted);
  overflow: hidden;
}
.score-bar i {
  display: block; height: 100%;
  background: linear-gradient(90deg, var(--color-accent), var(--color-accent-strong));
}
.policy-row small { display: block; margin-top: 0.25rem; color: var(--color-ink-muted); font-size: 11px; }
.tool-chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
.tool-chips span {
  padding: 0.2rem 0.45rem;
  border-radius: var(--radius-sm);
  background: var(--color-surface-muted);
  border: 1px solid var(--color-border);
  font-size: 11px;
}
.ticket-line { margin: 0 0 0.4rem; font-size: 12px; color: var(--color-ink-muted); }
.violation-list { margin: 0; padding-left: 1.1rem; color: var(--color-danger); font-size: 12px; }
.empty-decision {
  padding: 2rem 1rem;
  text-align: center;
  color: var(--color-ink-muted);
}
.empty-decision strong { display: block; color: var(--color-ink); margin-bottom: 0.35rem; }
.hitl-banner {
  display: flex; justify-content: space-between; gap: 0.75rem; align-items: center;
  margin-bottom: 0.65rem; padding: 0.65rem 0.75rem;
  border-radius: var(--radius-md);
  background: var(--color-warning-soft);
  border: 1px solid oklch(0.83 0.065 82);
}
.hitl-banner strong { display: block; font-size: var(--font-sm); }
.hitl-banner p { margin: 0.2rem 0 0; font-size: 11px; color: var(--color-ink-muted); }
.hitl-banner code { font-size: 11px; }
.lifecycle { display: grid; gap: 0.55rem; margin-top: 0.7rem; }
.lifecycle-step {
  display: grid; grid-template-columns: 14px 1fr auto; gap: 0.55rem; align-items: start;
  padding: 0.45rem 0.5rem; border-radius: var(--radius-md);
  background: var(--color-surface-muted); border: 1px solid var(--color-border);
}
.lifecycle-step .dot {
  width: 10px; height: 10px; margin-top: 4px; border-radius: 999px;
  background: var(--color-border-strong);
}
.lifecycle-step[data-status="done"] .dot { background: var(--color-success); }
.lifecycle-step[data-status="active"] .dot { background: var(--color-accent); box-shadow: 0 0 0 3px var(--color-accent-soft); }
.lifecycle-step strong { display: block; font-size: 12px; }
.lifecycle-step p { margin: 0.15rem 0 0; color: var(--color-ink-muted); font-size: 11px; }
.lifecycle-step em { font-style: normal; font-size: 10px; color: var(--color-ink-muted); text-transform: uppercase; }
.skeleton-decision { padding: 1rem 0.25rem; }
.skeleton-decision .sk-row {
  height: 14px; border-radius: 6px; margin-bottom: 0.65rem;
  background: linear-gradient(90deg, var(--color-surface-muted), var(--color-surface-strong), var(--color-surface-muted));
  background-size: 200% 100%;
  animation: shimmer 1.2s ease-in-out infinite;
}
.skeleton-decision .sk-row.short { width: 55%; }
.skeleton-decision p { margin: 0.5rem 0 0; color: var(--color-ink-muted); font-size: 12px; }
@keyframes shimmer {
  0% { background-position: 100% 0; }
  100% { background-position: -100% 0; }
}

.mobile-switch { display: none; gap: 0.35rem; margin-bottom: 0.75rem; }
.mobile-switch button {
  flex: 1; min-height: 34px; border-radius: var(--radius-md);
  border: 1px solid var(--color-border); background: var(--color-surface); cursor: pointer;
}
.mobile-switch button.active {
  background: var(--color-accent-soft);
  color: var(--color-accent-strong);
  border-color: oklch(0.82 0.055 225);
  font-weight: 650;
}

@media (max-width: 1100px) {
  .workspace-grid { grid-template-columns: 1fr; }
  .workflow-strip { grid-template-columns: 1fr 1fr; }
  .workflow-step { border-right: 0; border-bottom: 1px solid var(--color-border); }
  .conversation-panel, .decision-panel { height: auto; min-height: 520px; }
  .mobile-switch { display: flex; }
  .mobile-hidden { display: none !important; }
  .scenario-rail { order: 3; }
}
</style>
