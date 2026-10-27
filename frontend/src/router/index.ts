import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/setup', name: 'setup', component: () => import('@/views/SetupView.vue'), meta: { public: true } },
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    { path: '/', name: 'schedule', component: () => import('@/views/ScheduleView.vue') },
    { path: '/technicians', name: 'technicians', component: () => import('@/views/TechniciansView.vue') },
    { path: '/sites', name: 'sites', component: () => import('@/views/SitesView.vue') },
    { path: '/users', name: 'users', component: () => import('@/views/UsersView.vue'), meta: { admin: true } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.restore()
  // Until the first admin exists, the setup page is the whole app.
  if (auth.needsSetup) {
    return to.name === 'setup' ? true : { name: 'setup' }
  }
  if (to.name === 'setup') {
    return auth.isAuthenticated ? { name: 'schedule' } : { name: 'login' }
  }
  if (to.meta.public) {
    return auth.isAuthenticated ? { name: 'schedule' } : true
  }
  if (!auth.isAuthenticated) {
    return { name: 'login', query: to.fullPath === '/' ? {} : { next: to.fullPath } }
  }
  if (to.meta.admin && !auth.isAdmin) {
    return { name: 'schedule' }
  }
  return true
})

export default router
