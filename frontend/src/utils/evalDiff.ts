export type EvalDiffField = {
  label: string
  expected: string
  actual: string
  match: boolean
}

/** Build expected vs actual rows for console Eval diff view. */
export function buildEvalDiff(row: {
  expectedScenario?: string | null
  actualScenario?: string | null
  expectedAction?: string | null
  actualAction?: string | null
  expectedTools?: string[] | null
  actualTools?: string[] | null
  expectedMinRefund?: number | null
  actualRefund?: number | null
  expectedMaxFee?: number | null
  actualFee?: number | null
}): EvalDiffField[] {
  const fields: EvalDiffField[] = []

  const scenarioExp = row.expectedScenario ?? ''
  const scenarioAct = row.actualScenario ?? ''
  fields.push({
    label: 'scenario',
    expected: scenarioExp,
    actual: scenarioAct,
    match: scenarioExp === scenarioAct,
  })

  if (row.expectedAction || row.actualAction) {
    const exp = row.expectedAction ?? '—'
    const act = row.actualAction ?? '—'
    fields.push({
      label: 'action',
      expected: exp,
      actual: act,
      match: !row.expectedAction || exp === act,
    })
  }

  if (row.expectedTools?.length || row.actualTools?.length) {
    const exp = (row.expectedTools ?? []).join(' → ')
    const act = (row.actualTools ?? []).join(' → ')
    const match = !(row.expectedTools?.length)
      || isSubsequence(row.expectedTools!, row.actualTools ?? [])
    fields.push({ label: 'tools', expected: exp || '—', actual: act || '—', match })
  }

  if (row.expectedMinRefund != null || row.actualRefund != null) {
    const exp = row.expectedMinRefund != null ? `≥ ${row.expectedMinRefund}` : '—'
    const act = row.actualRefund != null ? String(row.actualRefund) : '—'
    const match = row.expectedMinRefund == null
      || (row.actualRefund != null && row.actualRefund >= row.expectedMinRefund)
    fields.push({ label: 'refund', expected: exp, actual: act, match })
  }

  if (row.expectedMaxFee != null || row.actualFee != null) {
    const exp = row.expectedMaxFee != null ? `≤ ${row.expectedMaxFee}` : '—'
    const act = row.actualFee != null ? String(row.actualFee) : '—'
    const match = row.expectedMaxFee == null
      || row.actualFee == null
      || row.actualFee <= row.expectedMaxFee
    fields.push({ label: 'fee', expected: exp, actual: act, match })
  }

  return fields
}

export function isSubsequence(expected: string[], actual: string[]): boolean {
  let i = 0
  for (const item of actual) {
    if (i < expected.length && item === expected[i]) i++
  }
  return i === expected.length
}
