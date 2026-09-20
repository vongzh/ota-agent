import { expect, test } from '@playwright/test'
import { installMockApi } from './fixtures/mock-api'

test.describe('StayOTA Agent browser smoke', () => {
  test.beforeEach(async ({ page }) => {
    if (process.env.E2E_LIVE !== '1') {
      await installMockApi(page)
    }
  })

  test('app loads with brand shell', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByText('StayOTA Agent').first()).toBeVisible()
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible()
    await expect(page.getByRole('button', { name: '智能处理台' })).toBeVisible()
    await expect(page.getByRole('button', { name: '框架调试台' })).toBeVisible()
  })

  test('workspace smoke + mocked agent happy path', async ({ page }) => {
    await page.goto('/workspace')
    await expect(page.getByRole('heading', { name: '智能处理台' })).toBeVisible()

    // Scenario rail from /api/scenarios
    await expect(page.getByText('取消退款（标准）')).toBeVisible()
    await expect(page.getByText('到账查询')).toBeVisible()

    // Auto-select scenario G triggers SSE /api/agent/message/stream
    await expect(page.getByText('E2E 演示酒店')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText(/可退款 ¥688/)).toBeVisible()
    await expect(page.getByText('标准取消退款')).toBeVisible()
    await expect(page.getByText('cancel_refund').first()).toBeVisible()
    await expect(page.getByText('Verifier 通过')).toBeVisible()

    // Health badge in shell (from /health)
    await expect(page.getByText(/2 场景 \/ 33 Tools/)).toBeVisible()
  })

  test('console smoke shows plugins and sessions panel', async ({ page }) => {
    await page.goto('/console')
    await expect(page.getByRole('heading', { name: '框架调试台' })).toBeVisible()
    await expect(page.getByText('refund').first()).toBeVisible()
    await expect(page.getByText('echo').first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sessions' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Agent Sessions' })).toBeVisible()
    await expect(page.getByText('暂无会话（先在处理台跑一轮对话）')).toBeVisible()
  })
})
