import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppConfig, DemoStatus, SetupStatus, User } from '@/api/types'
import { ApiError } from '@/api/client'
import router from '@/router'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/api', () => ({
  setupApi: { status: vi.fn<() => Promise<SetupStatus>>() },
  demoApi: { status: vi.fn<() => Promise<DemoStatus>>() },
  configApi: { get: vi.fn<() => Promise<AppConfig>>() },
  authApi: { me: vi.fn<() => Promise<User>>() },
}))

const planner: User = { id: 'u1', email: 'p@test.local', displayName: 'Pat', role: 'Planner', isDemo: false }

describe('router guard', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    const { setupApi, demoApi, configApi, authApi } = await import('@/api')
    vi.mocked(setupApi.status).mockResolvedValue({ needsSetup: false })
    vi.mocked(configApi.get).mockResolvedValue({ timeZone: 'UTC', passwordMinLength: 12 })
    vi.mocked(demoApi.status).mockResolvedValue({ enabled: false, resetMinutes: 0, nextResetAt: null })
    vi.mocked(authApi.me).mockRejectedValue(new ApiError(401, null))
  })

  it('sends the user to the login page, keeping the target, while the API is unreachable', async () => {
    const { setupApi } = await import('@/api')
    vi.mocked(setupApi.status).mockRejectedValue(new ApiError(502, null))

    await router.push('/technicians')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe('/technicians')
    expect(useAuthStore().serverDown).toBe(true)
  })

  it('asks a signed-out user to log in and keeps a planner off the admin page', async () => {
    await router.push('/sites')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe('/sites')

    useAuthStore().user = planner
    await router.push('/users')
    expect(router.currentRoute.value.name).toBe('schedule')
  })
})
