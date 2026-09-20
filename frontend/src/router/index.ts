import { createRouter, createWebHistory } from 'vue-router'
import BasicLayout from '@/layouts/BasicLayout.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: BasicLayout,
      redirect: '/workspace',
      children: [
        {
          path: 'design',
          name: 'Design',
          component: () => import('@/views/design/index.vue'),
          meta: { title: 'Agent 设计' },
        },
        {
          path: 'workspace',
          name: 'Workspace',
          component: () => import('@/views/workspace/index.vue'),
          meta: { title: '智能处理台' },
        },
        {
          path: 'dashboard',
          name: 'Dashboard',
          component: () => import('@/views/dashboard/index.vue'),
          meta: { title: '运营看板' },
        },
        {
          path: 'console',
          name: 'Console',
          component: () => import('@/views/console/index.vue'),
          meta: { title: '框架调试台' },
        },
      ],
    },
  ],
})

export default router
