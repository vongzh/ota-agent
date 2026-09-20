import axios from 'axios'
import type { AgentDecision, Scenario, ToolContract } from '@/types'
import { getScopeId } from '@/utils/scope'

// Prefer same-origin + Vite proxy so Cloud/port-forward previews work.
// Override with VITE_API_BASE_URL only when API is on another origin.
const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  timeout: 30000,
})

const apiKey = import.meta.env.VITE_API_KEY as string | undefined

http.interceptors.request.use((config) => {
  if (apiKey) config.headers['X-Api-Key'] = apiKey
  const scope = getScopeId()
  if (scope) config.headers['X-Scope-Id'] = scope
  return config
})

export async function fetchHosting() {
  const { data } = await http.get<{ demoEnabled: boolean; authRequired: boolean }>('/api/hosting')
  return data
}

export async function fetchScenarios() {
  const { data } = await http.get<Scenario[]>('/api/scenarios')
  return data
}

export async function fetchTools() {
  const { data } = await http.get<ToolContract[]>('/api/tools')
  return data
}

export async function runAgentMessage(payload: Record<string, unknown>) {
  const { data } = await http.post<AgentDecision>('/api/agent/message', payload)
  return data
}

export type AgentStreamHandlers = {
  onStep?: (text: string, data?: unknown) => void
  onTool?: (name: string) => void
  onReplyDelta?: (chunk: string) => void
  onApproval?: (sessionId: string | null | undefined, data?: unknown) => void
  onError?: (message: string) => void
}

/** SSE progressive message; resolves with final decision from `done` event. */
export async function runAgentMessageStream(
  payload: Record<string, unknown>,
  handlers: AgentStreamHandlers = {},
): Promise<AgentDecision> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'text/event-stream' }
  if (apiKey) headers['X-Api-Key'] = apiKey
  const scope = getScopeId()
  if (scope) headers['X-Scope-Id'] = scope
  const base = import.meta.env.VITE_API_BASE_URL || ''
  const res = await fetch(`${base}/api/agent/message/stream`, {
    method: 'POST',
    headers,
    body: JSON.stringify(payload),
  })
  if (!res.ok || !res.body) {
    throw new Error(`stream failed: ${res.status}`)
  }

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  let finalDecision: AgentDecision | null = null
  let eventName = 'message'

  const consumeBlock = (block: string) => {
    const lines = block.split('\n')
    let dataLine = ''
    for (const line of lines) {
      if (line.startsWith('event:')) eventName = line.slice(6).trim()
      if (line.startsWith('data:')) dataLine += line.slice(5).trim()
    }
    if (!dataLine) return
    let parsed: { type?: string; text?: string; data?: unknown }
    try {
      parsed = JSON.parse(dataLine)
    } catch {
      return
    }
    const type = parsed.type || eventName
    if (type === 'step') handlers.onStep?.(parsed.text || '', parsed.data)
    else if (type === 'tool') handlers.onTool?.(parsed.text || '')
    else if (type === 'reply_delta') handlers.onReplyDelta?.(parsed.text || '')
    else if (type === 'approval_required') handlers.onApproval?.(parsed.text, parsed.data)
    else if (type === 'error') handlers.onError?.(parsed.text || 'stream error')
    else if (type === 'done' && parsed.data) finalDecision = parsed.data as AgentDecision
  }

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    const parts = buffer.split('\n\n')
    buffer = parts.pop() || ''
    for (const part of parts) consumeBlock(part)
  }
  if (buffer.trim()) consumeBlock(buffer)

  if (!finalDecision) throw new Error('stream ended without decision')
  return finalDecision
}

export async function respondToApproval(payload: {
  sessionId: string
  requestId: string
  approved: boolean
  reason?: string
}) {
  const { data } = await http.post<AgentDecision>('/api/agent/approvals', payload)
  return data
}

export type EvalResultRow = {
  id: string
  message: string
  expectedScenario: string
  actualScenario: string
  passed: boolean
  detail?: string | null
  expectedAction?: string | null
  actualAction?: string | null
  expectedTools?: string[] | null
  actualTools?: string[] | null
  expectedMinRefund?: number | null
  actualRefund?: number | null
  expectedMaxFee?: number | null
  actualFee?: number | null
}

export async function runEval() {
  const { data } = await http.post('/api/eval/run')
  return data as { total: number; passed: number; failed: number; results: EvalResultRow[] }
}

export async function runAllWorkflows() {
  const { data } = await http.post('/api/workflows/run-all')
  return data as {
    total: number
    succeeded: number
    failed: number
    results: Array<{ scenarioId: string; succeeded: boolean; caseStatus: string; toolCalls: string[] }>
  }
}

export async function health() {
  const { data } = await http.get('/health')
  return data
}

export type AgentSessionSummary = {
  sessionId: string
  traceId: string
  userId: string
  orderId: string
  caseId: string
  scenarioId: string
  pendingApprovalCount: number
  updatedAt?: string
}

export type ToolAuditRow = {
  id: number
  traceId: string
  caseId?: string | null
  toolName: string
  access: string
  allowed: boolean
  denyReason?: string | null
  createdAt: string
}

export async function listSessions(take = 50) {
  const { data } = await http.get<AgentSessionSummary[]>('/api/agent/sessions', { params: { take } })
  return data
}

export async function getSession(sessionId: string) {
  const { data } = await http.get(`/api/agent/sessions/${encodeURIComponent(sessionId)}`)
  return data as {
    sessionId: string
    pendingApprovals?: Array<{
      requestId: string
      callId: string
      toolName: string
      arguments: Record<string, unknown>
      description: string
    }>
    [key: string]: unknown
  }
}

export async function deleteSession(sessionId: string) {
  await http.delete(`/api/agent/sessions/${encodeURIComponent(sessionId)}`)
}

export async function queryAudits(params: { traceId?: string; caseId?: string; take?: number }) {
  const { data } = await http.get<ToolAuditRow[]>('/api/agent/audits', { params })
  return data
}

export async function listPlugins() {
  const { data } = await http.get('/api/plugins')
  return data as Array<{ id: string; displayName: string; agentName: string; isPrimary: boolean }>
}

export async function listMcpTools() {
  const { data } = await http.get('/api/mcp/tools')
  return data as Array<{ name: string; source: string; access: string; purpose?: string }>
}

export async function invokeTool(payload: {
  toolName: string
  arguments?: Record<string, unknown>
  userId?: string
  conversationState?: string
}) {
  const { data } = await http.post('/api/tools/invoke', payload)
  return data as {
    allowed?: boolean
    success?: boolean
    toolName?: string
    denyReason?: string
    data?: unknown
    error?: string
  }
}

export async function getAiProvider() {
  const { data } = await http.get('/api/ai/provider')
  return data as {
    configuredProvider: string
    configuredModel?: string
    runtimeProvider?: string | null
    runtimeModel?: string | null
    effectiveProvider: string
    effectiveModel?: string
    allowed: string[]
  }
}

export async function setAiProvider(provider: string, model?: string) {
  const { data } = await http.post('/api/ai/provider', { provider, model })
  return data
}
