import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, http, setUnauthorizedHandler } from '@/api/client'

function respond(status: number, body: unknown) {
  return vi.fn<() => Promise<unknown>>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  })
}

describe('http client', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setUnauthorizedHandler(null)
  })

  it('sends JSON with same-origin credentials and parses the body', async () => {
    const fetchMock = respond(201, { id: '1' })
    vi.stubGlobal('fetch', fetchMock)

    const result = await http.post<{ id: string }>('/api/skills', { name: 'HVAC' })

    expect(result).toEqual({ id: '1' })
    expect(fetchMock).toHaveBeenCalledWith('/api/skills', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: '{"name":"HVAC"}',
    })
  })

  it('turns problem details into an ApiError with violations', async () => {
    vi.stubGlobal(
      'fetch',
      respond(409, {
        title: 'The technician cannot take this shift.',
        detail: 'Overlaps.',
        violations: [{ code: 'Overlap', message: 'Overlaps.', conflictingShiftId: 'x' }],
      }),
    )

    const error = await http.post('/api/shifts/1/assignments', {}).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(409)
    expect((error as ApiError).violations[0]?.code).toBe('Overlap')
  })

  it('notifies the unauthorized handler on 401, except for the login call itself', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    vi.stubGlobal('fetch', respond(401, null))

    await expect(http.get('/api/schedule/week')).rejects.toBeInstanceOf(ApiError)
    expect(handler).toHaveBeenCalledTimes(1)

    await expect(http.post('/api/auth/login', {})).rejects.toBeInstanceOf(ApiError)
    expect(handler).toHaveBeenCalledTimes(1)
  })
})
