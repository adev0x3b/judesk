import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SetupStatus, User } from '@/api/types'
import { ApiError } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const admin: User = { id: 'u1', email: 'boss@test.local', displayName: 'Boss', role: 'Admin' }

vi.mock('@/api', () => ({
  setupApi: {
    status: vi.fn<() => Promise<SetupStatus>>(),
    complete: vi.fn<() => Promise<User>>(),
  },
  authApi: {
    me: vi.fn<() => Promise<User>>(),
    login: vi.fn<() => Promise<User>>(),
    logout: vi.fn<() => Promise<void>>(),
  },
}))

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
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
})
