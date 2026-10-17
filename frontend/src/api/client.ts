import type { Violation } from './types'

/** A failed API call. Carries RFC 7807 problem details when the server sent them. */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | undefined
  readonly errors: Record<string, string[]>
  readonly violations: Violation[]

  constructor(status: number, body: Record<string, unknown> | null) {
    const title = (body?.title as string | undefined) ?? `Request failed (${status})`
    super(title)
    this.status = status
    this.title = title
    this.detail = body?.detail as string | undefined
    this.errors = (body?.errors as Record<string, string[]> | undefined) ?? {}
    this.violations = (body?.violations as Violation[] | undefined) ?? []
  }

  get isUnauthorized() {
    return this.status === 401
  }
}

export type OnUnauthorized = () => void
let onUnauthorized: OnUnauthorized | null = null

/** Lets the auth store react (clear the session) when any call comes back 401. */
export function setUnauthorizedHandler(handler: OnUnauthorized | null) {
  onUnauthorized = handler
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    let payload: Record<string, unknown> | null = null
    try {
      payload = (await response.json()) as Record<string, unknown>
    } catch {
      payload = null
    }
    if (response.status === 401 && !url.endsWith('/auth/login')) {
      onUnauthorized?.()
    }
    throw new ApiError(response.status, payload)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export const http = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T>(url: string, body: unknown) => request<T>('PUT', url, body),
  delete: <T = void>(url: string) => request<T>('DELETE', url),
}
