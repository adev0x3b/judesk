import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DemoStatus, SetupStatus, User } from '@/api/types'
import { ApiError } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const admin: User = { id: 'u1', email: 'boss@test.local', displayName: 'Boss', role: 'Admin' }

vi.mock('@/api', () => ({
  setupApi: {
    status: vi.fn<() => Promise<SetupStatus>>(),
    complete: vi.fn<() => Promise<User>>(),
  },
  demoApi: {
    status: vi.fn<() => Promise<DemoStatus>>(),
  },
  authApi: {
    me: vi.fn<() => Promise<User>>(),
    login: vi.fn<() => Promise<User>>(),
    loginDemo: vi.fn<() => Promise<User>>(),
    logout: vi.fn<() => Promise<void>>(),
  },
}))

const demoOff: DemoStatus = { enabled: false, resetMinutes: 0, nextResetAt: null }

describe('auth store', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    const { demoApi } = await import('@/api')
    vi.mocked(demoApi.status).mockResolvedValue(demoOff)
  })

  it('reports setup pending and asks nothing else of the API', async () => {
    const { setupApi, authApi } = await import('@/api')
    vi.mocked(setupApi.status).mockResolvedValueOnce({ needsSetup: true })
    const store = useAuthStore()

    await store.restore()

    expect(store.needsSetup).toBe(true)
    expect(store.isAuthenticated).toBe(false)
    expect(authApi.me).not.toHaveBeenCalled()
  })

  it('restores the session once setup is done', async () => {
    const { setupApi, authApi } = await import('@/api')
    vi.mocked(setupApi.status).mockResolvedValueOnce({ needsSetup: false })
    vi.mocked(authApi.me).mockResolvedValueOnce(admin)
    const store = useAuthStore()

    await store.restore()

    expect(store.needsSetup).toBe(false)
    expect(store.user).toEqual(admin)
    expect(store.isAdmin).toBe(true)
  })

  it('treats a 401 on restore as signed out, not as an error', async () => {
    const { setupApi, authApi } = await import('@/api')
    vi.mocked(setupApi.status).mockResolvedValueOnce({ needsSetup: false })
    vi.mocked(authApi.me).mockRejectedValueOnce(new ApiError(401, null))
    const store = useAuthStore()

    await store.restore()

    expect(store.user).toBeNull()
    expect(store.checked).toBe(true)
  })

  it('opens the app after setup completes', () => {
    const store = useAuthStore()
    store.needsSetup = true

    store.completedSetup(admin)

    expect(store.needsSetup).toBe(false)
    expect(store.isAuthenticated).toBe(true)
  })

  it('exposes the demo status and signs in without a password', async () => {
    const { setupApi, authApi, demoApi } = await import('@/api')
    vi.mocked(setupApi.status).mockResolvedValueOnce({ needsSetup: false })
    vi.mocked(demoApi.status).mockResolvedValueOnce({ enabled: true, resetMinutes: 60, nextResetAt: '2030-01-01T10:00:00Z' })
    vi.mocked(authApi.me).mockRejectedValueOnce(new ApiError(401, null))
    const planner: User = { id: 'u2', email: 'demo@judesk.local', displayName: 'Demo planner', role: 'Planner' }
    vi.mocked(authApi.loginDemo).mockResolvedValueOnce(planner)
    const store = useAuthStore()

    await store.restore()
    expect(store.demo.enabled).toBe(true)
    expect(store.isAuthenticated).toBe(false)

    await store.loginDemo()
    expect(store.user).toEqual(planner)
    expect(store.isAdmin).toBe(false)
  })
})
