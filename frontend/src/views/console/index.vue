<template>
  <div class="console">
    <div class="workspace-heading">
      <div>
        <h1>框架调试台</h1>
        <p>Session · Tool Audit · MCP · Provider — 验证多插件与运行时运维面。</p>
      </div>
      <div class="heading-actions">
        <span v-for="p in plugins" :key="p.id" class="status-pill" :class="p.isPrimary ? 'success' : 'neutral'">
          {{ p.id }}{{ p.isPrimary ? ' · primary' : '' }}
        </span>
      </div>
    </div>

    <div class="tabs">
      <button
        v-for="t in tabs"
        :key="t.id"
        class="tab"
        :class="{ 'is-active': tab === t.id }"
        @click="tab = t.id"
      >{{ t.label }}</button>
    </div>

    <p v-if="errorText" class="banner danger">{{ errorText }}</p>

    <!-- Sessions -->
    <section v-if="tab === 'sessions'" class="panel pad">
      <div class="section-head">
        <h2>Agent Sessions</h2>
        <button class="ghost-btn" :disabled="loading" @click="loadSessions">刷新</button>
      </div>
      <div v-if="!sessions.length" class="empty">暂无会话（先在处理台跑一轮对话）</div>
      <table v-else class="grid-table">
        <thead>
          <tr>
            <th>Session</th>
            <th>Scenario</th>
            <th>Case</th>
            <th>Trace</th>
            <th>Pending</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in sessions" :key="s.sessionId">
            <td><code>{{ s.sessionId }}</code></td>
            <td>{{ s.scenarioId }}</td>
            <td>{{ s.caseId }}</td>
            <td><code>{{ s.traceId }}</code></td>
            <td>{{ s.pendingApprovalCount }}</td>
            <td class="row-actions">
              <button class="ghost-btn" @click="inspectSession(s.sessionId)">详情</button>
              <button class="ghost-btn" @click="removeSession(s.sessionId)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
      <pre v-if="sessionDetail" class="code-block">{{ sessionDetail }}</pre>
    </section>

    <!-- Audits -->
    <section v-if="tab === 'audits'" class="panel pad">
      <div class="section-head">
        <h2>Tool Audit</h2>
      </div>
      <div class="filters">
        <input v-model="auditTraceId" placeholder="traceId" />
        <input v-model="auditCaseId" placeholder="caseId" />
        <button class="primary-btn" :disabled="loading" @click="loadAudits">查询</button>
      </div>
      <div v-if="!audits.length" class="empty">输入 traceId 或 caseId 后查询</div>
      <table v-else class="grid-table">
        <thead>
          <tr>
            <th>Time</th>
            <th>Tool</th>
            <th>Allowed</th>
            <th>Deny</th>
            <th>Trace</th>
            <th>Case</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="a in audits" :key="a.id">
            <td>{{ formatTime(a.createdAt) }}</td>
            <td><code>{{ a.toolName }}</code></td>
            <td>
              <span class="status-pill" :class="a.allowed ? 'success' : 'danger'">
                {{ a.allowed ? 'yes' : 'no' }}
              </span>
            </td>
            <td>{{ a.denyReason || '—' }}</td>
            <td><code>{{ a.traceId }}</code></td>
            <td>{{ a.caseId || '—' }}</td>
          </tr>
        </tbody>
      </table>
    </section>

    <!-- MCP -->
    <section v-if="tab === 'mcp'" class="panel pad">
      <div class="section-head">
        <h2>MCP / Tool Inspector</h2>
        <button class="ghost-btn" :disabled="loading" @click="loadMcpTools">刷新工具</button>
      </div>
      <p class="hint">列出 Refund 契约工具 + Echo 样例；试调走 <code>POST /api/tools/invoke</code>（Demo）。MCP 端点 <code>/mcp</code>。</p>
      <div class="split">
        <ul class="tool-list">
          <li
            v-for="t in mcpTools"
            :key="t.name"
            :class="{ 'is-active': invokeName === t.name }"
            @click="selectTool(t)"
          >
            <strong>{{ t.name }}</strong>
            <small>{{ t.source }} · {{ t.access }}</small>
          </li>
        </ul>
        <div class="invoke-pane">
          <label>Tool</label>
          <input v-model="invokeName" />
          <label>Arguments (JSON)</label>
          <textarea v-model="invokeArgs" rows="6" />
          <button class="primary-btn" :disabled="loading || !invokeName" @click="runInvoke">试调</button>
          <pre v-if="invokeResult" class="code-block">{{ invokeResult }}</pre>
        </div>
      </div>
    </section>

    <!-- Provider -->
    <section v-if="tab === 'provider'" class="panel pad">
      <div class="section-head">
        <h2>AI Provider</h2>
        <button class="ghost-btn" :disabled="loading" @click="loadProvider">刷新</button>
      </div>
      <div v-if="provider" class="provider-card">
        <p>Configured: <strong>{{ provider.configuredProvider }}</strong>
          <span v-if="provider.configuredModel"> / {{ provider.configuredModel }}</span></p>
        <p>Effective: <strong>{{ provider.effectiveProvider }}</strong>
          <span v-if="provider.effectiveModel"> / {{ provider.effectiveModel }}</span></p>
        <div class="filters">
          <select v-model="providerDraft">
            <option v-for="a in provider.allowed" :key="a" :value="a">{{ a }}</option>
          </select>
          <input v-model="modelDraft" placeholder="model（可选）" />
          <button class="primary-btn" :disabled="loading" @click="applyProvider">切换（Demo）</button>
        </div>
        <p class="hint">下次请求生效；缺少 OpenAI/Ollama 密钥时会回退 Deterministic（若允许）。</p>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  deleteSession,
  getAiProvider,
  getSession,
  invokeTool,
  listMcpTools,
  listPlugins,
  listSessions,
  queryAudits,
  setAiProvider,
  type AgentSessionSummary,
  type ToolAuditRow,
} from '@/api/agent'

const tabs = [
  { id: 'sessions', label: 'Sessions' },
  { id: 'audits', label: 'Audits' },
  { id: 'mcp', label: 'MCP Inspector' },
  { id: 'provider', label: 'Provider' },
] as const

const tab = ref<(typeof tabs)[number]['id']>('sessions')
const loading = ref(false)
const errorText = ref('')
const plugins = ref<Array<{ id: string; displayName: string; agentName: string; isPrimary: boolean }>>([])

const sessions = ref<AgentSessionSummary[]>([])
const sessionDetail = ref('')

const auditTraceId = ref('')
const auditCaseId = ref('')
const audits = ref<ToolAuditRow[]>([])

const mcpTools = ref<Array<{ name: string; source: string; access: string; purpose?: string }>>([])
const invokeName = ref('echo_ping')
const invokeArgs = ref('{}')
const invokeResult = ref('')

const provider = ref<Awaited<ReturnType<typeof getAiProvider>> | null>(null)
const providerDraft = ref('Deterministic')
const modelDraft = ref('')

function formatTime(iso: string) {
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

async function wrap(fn: () => Promise<void>) {
  loading.value = true
  errorText.value = ''
  try {
    await fn()
  } catch (e: unknown) {
    const err = e as { response?: { data?: { message?: string } }; message?: string }
    errorText.value = err.response?.data?.message || err.message || '请求失败'
  } finally {
    loading.value = false
  }
}

async function loadPlugins() {
  plugins.value = await listPlugins()
}

async function loadSessions() {
  await wrap(async () => {
    sessions.value = await listSessions()
  })
}

async function inspectSession(id: string) {
  await wrap(async () => {
    sessionDetail.value = JSON.stringify(await getSession(id), null, 2)
  })
}

async function removeSession(id: string) {
  await wrap(async () => {
    await deleteSession(id)
    sessionDetail.value = ''
    sessions.value = await listSessions()
  })
}

async function loadAudits() {
  await wrap(async () => {
    if (!auditTraceId.value && !auditCaseId.value) {
      errorText.value = '需要 traceId 或 caseId'
      return
    }
    audits.value = await queryAudits({
      traceId: auditTraceId.value || undefined,
      caseId: auditCaseId.value || undefined,
    })
  })
}

async function loadMcpTools() {
  await wrap(async () => {
    mcpTools.value = await listMcpTools()
  })
}

function selectTool(t: { name: string; source: string }) {
  invokeName.value = t.name
  invokeArgs.value = t.name === 'echo_reflect'
    ? JSON.stringify({ message: 'hello from console' }, null, 2)
    : '{}'
}

async function runInvoke() {
  await wrap(async () => {
    let args: Record<string, unknown> = {}
    try { args = JSON.parse(invokeArgs.value || '{}') } catch {
      errorText.value = 'Arguments 不是合法 JSON'
      return
    }
    const result = await invokeTool({
      toolName: invokeName.value,
      arguments: args,
      conversationState: invokeName.value.startsWith('echo_') ? 'INTENT_READY' : 'DECISION_READY',
    })
    invokeResult.value = JSON.stringify(result, null, 2)
  })
}

async function loadProvider() {
  await wrap(async () => {
    provider.value = await getAiProvider()
    providerDraft.value = provider.value.effectiveProvider || 'Deterministic'
    modelDraft.value = provider.value.effectiveModel || ''
  })
}

async function applyProvider() {
  await wrap(async () => {
    await setAiProvider(providerDraft.value, modelDraft.value || undefined)
    await loadProvider()
  })
}

onMounted(async () => {
  try {
    await loadPlugins()
  } catch { /* ignore */ }
  await loadSessions()
  await loadMcpTools()
  await loadProvider()
})
</script>

<style scoped>
.console { display: flex; flex-direction: column; gap: 1rem; }
.tabs { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.tab {
  border: 1px solid var(--border, #d8dde6);
  background: transparent;
  padding: 0.45rem 0.9rem;
  border-radius: 6px;
  cursor: pointer;
}
.tab.is-active { background: var(--text, #1a2333); color: #fff; border-color: transparent; }
.filters { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-bottom: 1rem; }
.filters input, .filters select, .invoke-pane input, .invoke-pane textarea {
  border: 1px solid var(--border, #d8dde6);
  border-radius: 6px;
  padding: 0.45rem 0.65rem;
  min-width: 10rem;
  font: inherit;
}
.grid-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
.grid-table th, .grid-table td {
  text-align: left;
  padding: 0.55rem 0.4rem;
  border-bottom: 1px solid var(--border, #e6e9ef);
}
.row-actions { display: flex; gap: 0.35rem; }
.code-block {
  margin-top: 1rem;
  padding: 0.85rem;
  background: #0f172a0d;
  border-radius: 8px;
  overflow: auto;
  font-size: 0.8rem;
}
.empty, .hint { color: #667085; font-size: 0.9rem; }
.split { display: grid; grid-template-columns: minmax(12rem, 1fr) 1.4fr; gap: 1rem; }
@media (max-width: 800px) { .split { grid-template-columns: 1fr; } }
.tool-list { list-style: none; margin: 0; padding: 0; max-height: 22rem; overflow: auto; }
.tool-list li {
  padding: 0.55rem 0.65rem;
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}
.tool-list li:hover, .tool-list li.is-active { background: #0f172a0d; }
.tool-list small { color: #667085; }
.invoke-pane { display: flex; flex-direction: column; gap: 0.4rem; }
.provider-card p { margin: 0.35rem 0; }
.banner.danger { background: #fef3f2; color: #b42318; padding: 0.65rem 0.85rem; border-radius: 8px; }
</style>
