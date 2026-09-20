import { describe, expect, it } from 'vitest'
import { buildEvalDiff, isSubsequence } from './evalDiff'

describe('evalDiff', () => {
  it('detects tool subsequence', () => {
    expect(isSubsequence(['a', 'c'], ['a', 'b', 'c'])).toBe(true)
    expect(isSubsequence(['a', 'c'], ['a', 'b'])).toBe(false)
  })

  it('builds expected/actual rows', () => {
    const rows = buildEvalDiff({
      expectedScenario: 'G',
      actualScenario: 'G',
      expectedAction: 'ConfirmCancel',
      actualAction: 'ConfirmCancel',
      expectedTools: ['get_order_detail', 'calculate_refund_quote'],
      actualTools: ['list_user_orders', 'get_order_detail', 'calculate_refund_quote'],
      expectedMinRefund: 100,
      actualRefund: 120,
    })
    expect(rows.every((r) => r.match)).toBe(true)
    expect(rows.find((r) => r.label === 'tools')?.expected).toContain('get_order_detail')
  })

  it('flags scenario mismatch', () => {
    const rows = buildEvalDiff({ expectedScenario: 'A', actualScenario: 'B' })
    expect(rows[0].match).toBe(false)
  })
})
