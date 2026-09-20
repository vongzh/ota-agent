const STORAGE_KEY = 'stayota.demo.scopeId'

/** Demo-only multi-customer isolation key (maps to X-Scope-Id). */
export function getScopeId(): string {
  try {
    return (localStorage.getItem(STORAGE_KEY) || '').trim()
  } catch {
    return ''
  }
}

export function setScopeId(value: string) {
  const cleaned = value.trim().slice(0, 64)
  try {
    if (!cleaned) localStorage.removeItem(STORAGE_KEY)
    else localStorage.setItem(STORAGE_KEY, cleaned)
  } catch {
    /* ignore */
  }
}
