import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppConfig } from '@/api/types'
import { ApiError } from '@/api/client'
import { useConfigStore } from '@/stores/config'

vi.mock('@/api', () => ({
  configApi: { get: vi.fn<() => Promise<AppConfig>>() },
}))

describe('config store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts with UTC and the default password length', () => {
    const store = useConfigStore()
    expect(store.timeZone).toBe('UTC')
    expect(store.passwordMinLength).toBe(12)
  })

  it('takes the installation settings from the API, once', async () => {
    const { configApi } = await import('@/api')
    vi.mocked(configApi.get).mockResolvedValue({ timeZone: 'Europe/Rome', passwordMinLength: 10 })
    const store = useConfigStore()

    await store.load()
    await store.load()

    expect(store.timeZone).toBe('Europe/Rome')
    expect(store.passwordMinLength).toBe(10)
    expect(configApi.get).toHaveBeenCalledTimes(1)
  })

  it('keeps the defaults when the call fails and tries again next time', async () => {
    const { configApi } = await import('@/api')
    vi.mocked(configApi.get).mockRejectedValueOnce(new ApiError(502, null))
    const store = useConfigStore()

    await store.load()
    expect(store.timeZone).toBe('UTC')
    expect(store.loaded).toBe(false)

    vi.mocked(configApi.get).mockResolvedValueOnce({ timeZone: 'Europe/Rome', passwordMinLength: 12 })
    await store.load()
    expect(store.timeZone).toBe('Europe/Rome')
    expect(store.loaded).toBe(true)
  })

  it('falls back to UTC for a zone this runtime does not know', async () => {
    const { configApi } = await import('@/api')
    vi.mocked(configApi.get).mockResolvedValueOnce({ timeZone: 'Mars/Olympus', passwordMinLength: 0 })
    const store = useConfigStore()

    await store.load()

    expect(store.timeZone).toBe('UTC')
    expect(store.passwordMinLength).toBe(12)
  })
})
