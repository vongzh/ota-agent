<template>
  <div class="design">
    <div class="workspace-heading">
      <div>
        <h1>Agent 设计</h1>
        <p>对齐 StayOTA Agent Workflow：READ/WRITE Gate、回环追问、交易/协同双路径与 Verifier。</p>
      </div>
      <div class="heading-actions">
        <button class="ghost-btn" :disabled="loading" @click="runWorkflows">跑 A–L Workflow</button>
        <button class="primary-btn" @click="$router.push('/workspace')">开始场景模拟</button>
      </div>
    </div>

    <div v-if="summary" class="banner success">{{ summary }}</div>

    <FlowDiagram class="panel" />

    <div class="two">
      <section class="panel pad">
        <div class="section-head">
          <h2>33 Tool 契约</h2>
          <span class="status-pill info">{{ tools.length }} registered</span>
        </div>
        <div class="tool-table">
          <div class="tool-row head">
            <span>Tool</span><span>模式</span><span>允许状态</span><span>用途</span>
          </div>
          <div class="tool-row" v-for="t in tools.slice(0, 14)" :key="t.name">
            <strong>{{ t.name }}</strong>
            <span class="status-pill" :class="t.mode === 'write' ? 'warning' : 'neutral'">{{ t.mode }}</span>
            <span class="states">{{ (t.allowedConversationStates || []).slice(0, 2).join(' · ') }}</span>
            <span class="purpose">{{ t.purpose }}</span>
          </div>
          <p class="more" v-if="tools.length > 14">其余 {{ tools.length - 14 }} 个 Tool 经 AIFunction + ApprovalGateway 门禁。</p>
        </div>
      </section>

      <section class="panel pad">
        <h2>设计边界</h2>
        <ul class="guardrails">
          <li>成交政策快照优先于聊天记忆</li>
          <li>写操作需要确认令牌、订单版本、幂等键</li>
          <li>Tool 仅允许在契约声明的 conversation state 执行</li>
          <li>L3 禁止自动资金写，必须人工升级</li>
          <li>Verifier 校验金额 / 风险 / 话术事实边界</li>
          <li>A–L 由 Microsoft Agent Framework Workflows 回放</li>
          <li>IChatClient 可切换 Deterministic / OpenAI / Ollama</li>
        </ul>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import { fetchTools, runAllWorkflows } from '@/api/agent'
import type { ToolContract } from '@/types'
import FlowDiagram from '@/components/FlowDiagram.vue'

const tools = ref<ToolContract[]>([])
const loading = ref(false)
const summary = ref('')

onMounted(async () => {
  try { tools.value = await fetchTools() } catch { tools.value = [] }
})

async function runWorkflows() {
  loading.value = true
  try {
    const res = await runAllWorkflows()
    summary.value = `A–L Workflow ${res.succeeded}/${res.total} succeeded · Agent Framework`
    message.success(summary.value)
  } catch {
    message.error('Workflow 运行失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.workspace-heading {
  display: flex; align-items: flex-end; justify-content: space-between; gap: 1rem; margin-bottom: 1rem;
}
.workspace-heading h1 { margin: 0 0 0.2rem; font-size: 1.55rem; letter-spacing: -0.02em; }
.workspace-heading p { margin: 0; color: var(--color-ink-muted); font-size: var(--font-sm); }
.heading-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.ghost-btn, .primary-btn {
  min-height: 36px; padding: 0 0.95rem; border-radius: var(--radius-md); cursor: pointer; font-weight: 600;
}
.ghost-btn { border: 1px solid var(--color-border); background: var(--color-surface); }
.primary-btn { border: 0; background: var(--color-accent-strong); color: #fff; }
.banner {
  margin-bottom: 0.85rem; padding: 0.7rem 0.9rem; border-radius: var(--radius-md);
  border: 1px solid oklch(0.83 0.055 155); background: var(--color-success-soft); color: var(--color-success);
  font-size: var(--font-sm); font-weight: 600;
}
.two { display: grid; grid-template-columns: 1.6fr 1fr; gap: 1rem; margin-top: 1rem; }
.pad { padding: 1rem; }
.section-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem; }
.section-head h2, .pad > h2 { margin: 0 0 0.75rem; font-size: 1rem; }
.tool-table { display: grid; gap: 0.35rem; }
.tool-row {
  display: grid; grid-template-columns: 1.3fr 0.55fr 1fr 1.4fr; gap: 0.5rem; align-items: center;
  padding: 0.55rem 0.4rem; border-bottom: 1px solid var(--color-border); font-size: 12px;
}
.tool-row.head { color: var(--color-ink-muted); font-size: 11px; border-bottom-color: var(--color-border-strong); }
.states, .purpose { color: var(--color-ink-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.more { margin: 0.6rem 0 0; color: var(--color-ink-muted); font-size: 12px; }
.guardrails { margin: 0; padding-left: 1.1rem; line-height: 1.85; color: var(--color-ink); font-size: var(--font-sm); }
@media (max-width: 1100px) {
  .two { grid-template-columns: 1fr; }
  .tool-row { grid-template-columns: 1fr 0.5fr; }
  .tool-row span:nth-child(3), .tool-row span:nth-child(4) { display: none; }
}
</style>
