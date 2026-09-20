<template>
  <div class="app-shell">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">旅</span>
        <div>
          <strong>StayOTA Agent</strong>
          <small>酒店 OTA Agent · 退款演示</small>
        </div>
      </div>
      <nav class="primary-nav" aria-label="主导航">
        <button
          v-for="item in nav"
          :key="item.path"
          class="nav-item"
          :class="{ 'is-active': route.path === item.path }"
          @click="router.push(item.path)"
        >
          {{ item.label }}
        </button>
      </nav>
      <div class="demo-badge">
        <span class="health">{{ healthText }}</span>
        <span>{{ modeLabel }}</span>
      </div>
    </header>
    <main class="page-frame">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { health } from '@/api/agent'

const route = useRoute()
const router = useRouter()
const healthText = ref('连接中…')
const modeLabel = ref('…')

const nav = [
  { path: '/design', label: 'Agent 设计' },
  { path: '/workspace', label: '智能处理台' },
  { path: '/dashboard', label: '运营看板' },
]

onMounted(async () => {
  try {
    const h = await health() as {
      scenarios?: number
      tools?: number
      aiProvider?: string
      agent?: string
      demoEnabled?: boolean
      productionMode?: string
      authRequired?: boolean
    }
    healthText.value = h.scenarios != null && h.tools != null
      ? `${h.scenarios} 场景 / ${h.tools} Tools`
      : (h.agent || '已连接')
    const bits = [
      h.demoEnabled === false ? '正式' : 'Demo',
      h.productionMode || 'Mock',
      h.authRequired ? 'Auth' : null,
    ].filter(Boolean)
    modeLabel.value = bits.join(' · ')
  } catch {
    healthText.value = '后端未连接'
    modeLabel.value = '离线'
  }
})
</script>
