<template>
  <div class="dashboard">
    <div class="workspace-heading">
      <div>
        <h1>运营看板</h1>
        <p>北极星、漏斗健康度与 Badcase 闭环 — 对齐 StayOTA 运营视图（Demo 示意）。</p>
      </div>
      <div class="heading-actions">
        <button class="ghost-btn" :disabled="wfLoading" @click="runWorkflowBatch">跑 A–L Workflow</button>
        <button class="primary-btn" :disabled="evalLoading" @click="runOfflineEval">跑 36 条 Eval</button>
      </div>
    </div>

    <div v-if="evalSummary || wfSummary" class="banners">
      <div v-if="evalSummary" class="banner success">{{ evalSummary }}</div>
      <div v-if="wfSummary" class="banner info">{{ wfSummary }}</div>
    </div>

    <section class="northstar panel">
      <div>
        <span class="eyebrow">北极星 · Demo</span>
        <h2>正确退款任务闭环率</h2>
        <p>结果、金额、权限与流程均正确，且取消 / 退款 / 替代方案得到确认。</p>
      </div>
      <div class="northstar-value">
        <strong>62.4%</strong>
        <span class="status-pill warning">距目标 -12.6pp</span>
      </div>
    </section>

    <section class="metric-band">
      <article class="panel metric" v-for="item in metrics" :key="item.name">
        <span>{{ item.group }}</span>
        <h3>{{ item.name }}</h3>
        <strong>{{ item.value }}</strong>
        <p>{{ item.note }}</p>
        <i class="trend" :data-tone="item.tone" />
      </article>
    </section>

    <section class="panel pad">
      <div class="section-head">
        <h2>处理漏斗</h2>
        <span class="status-pill neutral">示意数据</span>
      </div>
      <div class="funnel">
        <div class="funnel-row" v-for="row in funnel" :key="row.stage">
          <div class="funnel-label">
            <strong>{{ row.stage }}</strong>
            <small>流失 {{ row.drop }} · {{ row.note }}</small>
          </div>
          <div class="funnel-track">
            <i :style="{ width: `${(row.in / 10000) * 100}%` }" />
          </div>
          <div class="funnel-value">{{ row.in.toLocaleString() }}</div>
        </div>
      </div>
    </section>

    <section class="panel pad">
      <div class="section-head">
        <h2>Badcase 生命周期</h2>
        <div class="lifecycle">
          <span v-for="s in stages" :key="s" class="status-pill neutral">{{ s }}</span>
        </div>
      </div>
      <div class="bad-grid">
        <article v-for="b in badcases" :key="b.type" class="bad-card">
          <div class="bad-top">
            <strong>{{ b.type }}</strong>
            <span class="status-pill" :class="b.risk === '高' ? 'danger' : 'warning'">{{ b.risk }}</span>
          </div>
          <p>{{ b.count }} 件 · Owner {{ b.owner }}</p>
          <div class="stage-chip">{{ b.stage }}</div>
        </article>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { message } from 'ant-design-vue'
import { runAllWorkflows, runEval } from '@/api/agent'

const evalLoading = ref(false)
const wfLoading = ref(false)
const evalSummary = ref('')
const wfSummary = ref('')
const stages = ['发现', '修复', '回归验证', '关闭']

const metrics = [
  { group: '结果', name: '一次解决率', value: '71.2%', note: '未达目标，需压缩二次进线', tone: 'warn' },
  { group: '风险', name: '错误承诺率', value: '1.8%', note: '政策冲突拦截有效', tone: 'ok' },
  { group: '效率', name: '自动处理覆盖率', value: '54.6%', note: 'L1 场景可继续提升', tone: 'warn' },
  { group: '能力', name: '规则判断准确率', value: '96.1%', note: '金额与权限不交给模型', tone: 'ok' },
]

const funnel = [
  { stage: '进线识别', in: 10000, drop: 320, note: '低置信度澄清' },
  { stage: '订单确认', in: 9680, drop: 410, note: '多订单歧义' },
  { stage: '规则决策', in: 9270, drop: 880, note: '信息不足' },
  { stage: '外部协同', in: 8390, drop: 1310, note: '供应商/支付等待' },
  { stage: '结果确认', in: 7080, drop: 840, note: '到账未确认' },
]

const badcases = [
  { type: '越权承诺退款', count: 12, risk: '高', stage: '修复', owner: '规则引擎' },
  { type: '到账预期管理失败', count: 31, risk: '中', stage: '回归验证', owner: '支付协同' },
  { type: '人工摘要缺失', count: 7, risk: '中', stage: '发现', owner: 'HITL' },
  { type: '团体写操作未阻断', count: 3, risk: '高', stage: '关闭', owner: '权限门' },
]

async function runOfflineEval() {
  evalLoading.value = true
  try {
    const res = await runEval()
    evalSummary.value = `Eval ${res.passed}/${res.total} passed，失败 ${res.failed}`
    message.success(evalSummary.value)
  } catch {
    message.error('Eval 运行失败')
  } finally {
    evalLoading.value = false
  }
}

async function runWorkflowBatch() {
  wfLoading.value = true
  try {
    const res = await runAllWorkflows()
    wfSummary.value = `Workflow ${res.succeeded}/${res.total} succeeded`
    message.success(wfSummary.value)
  } catch {
    message.error('Workflow 运行失败')
  } finally {
    wfLoading.value = false
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
.banners { display: grid; gap: 0.5rem; margin-bottom: 0.85rem; }
.banner {
  padding: 0.7rem 0.9rem; border-radius: var(--radius-md); font-size: var(--font-sm); font-weight: 600;
}
.banner.success { background: var(--color-success-soft); color: var(--color-success); border: 1px solid oklch(0.83 0.055 155); }
.banner.info { background: var(--color-accent-soft); color: var(--color-accent-strong); border: 1px solid oklch(0.82 0.055 225); }

.northstar {
  display: flex; justify-content: space-between; align-items: center; gap: 1.5rem;
  padding: 1.25rem 1.35rem; margin-bottom: 1rem;
  background:
    linear-gradient(135deg, oklch(0.94 0.03 225 / 0.9), transparent 55%),
    var(--color-surface);
}
.eyebrow { font-size: 11px; color: var(--color-ink-muted); letter-spacing: 0.04em; text-transform: uppercase; }
.northstar h2 { margin: 0.25rem 0 0.35rem; font-size: 1.25rem; }
.northstar p { margin: 0; color: var(--color-ink-muted); font-size: var(--font-sm); max-width: 42rem; }
.northstar-value { text-align: right; display: grid; gap: 0.45rem; justify-items: end; }
.northstar-value strong { font-size: 2.4rem; letter-spacing: -0.03em; line-height: 1; color: var(--color-accent-strong); }

.metric-band {
  display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.85rem; margin-bottom: 1rem;
}
.metric { padding: 1rem; position: relative; overflow: hidden; }
.metric span { font-size: 11px; color: var(--color-ink-muted); }
.metric h3 { margin: 0.35rem 0 0.45rem; font-size: var(--font-sm); font-weight: 650; }
.metric strong { font-size: 1.45rem; letter-spacing: -0.02em; }
.metric p { margin: 0.45rem 0 0; color: var(--color-ink-muted); font-size: 12px; }
.trend {
  position: absolute; right: 0; bottom: 0; width: 42%; height: 3px;
  background: var(--color-success);
}
.trend[data-tone="warn"] { background: var(--color-warning); }

.pad { padding: 1rem 1.1rem 1.15rem; margin-bottom: 1rem; }
.section-head {
  display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; margin-bottom: 0.9rem;
}
.section-head h2 { margin: 0; font-size: 1rem; }
.lifecycle { display: flex; flex-wrap: wrap; gap: 0.35rem; }

.funnel { display: grid; gap: 0.75rem; }
.funnel-row {
  display: grid; grid-template-columns: 180px 1fr 72px; gap: 0.75rem; align-items: center;
}
.funnel-label strong { display: block; font-size: var(--font-sm); }
.funnel-label small { color: var(--color-ink-muted); font-size: 11px; }
.funnel-track {
  height: 12px; border-radius: 999px; background: var(--color-surface-muted); overflow: hidden;
  border: 1px solid var(--color-border);
}
.funnel-track i {
  display: block; height: 100%;
  background: linear-gradient(90deg, var(--color-accent), var(--color-accent-strong));
}
.funnel-value { text-align: right; font-weight: 650; font-size: var(--font-sm); }

.bad-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.75rem; }
.bad-card {
  padding: 0.85rem; border: 1px solid var(--color-border); border-radius: var(--radius-md);
  background: var(--color-surface-muted);
}
.bad-top { display: flex; justify-content: space-between; gap: 0.5rem; align-items: start; margin-bottom: 0.4rem; }
.bad-top strong { font-size: var(--font-sm); }
.bad-card p { margin: 0 0 0.55rem; color: var(--color-ink-muted); font-size: 12px; }
.stage-chip {
  display: inline-flex; padding: 0.15rem 0.45rem; border-radius: var(--radius-pill);
  background: var(--color-surface); border: 1px solid var(--color-border); font-size: 11px;
}

@media (max-width: 1100px) {
  .metric-band, .bad-grid { grid-template-columns: 1fr 1fr; }
  .funnel-row { grid-template-columns: 1fr; }
  .northstar { flex-direction: column; align-items: flex-start; }
  .northstar-value { justify-items: start; text-align: left; }
}
</style>
