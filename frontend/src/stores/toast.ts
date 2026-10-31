import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ToastKind = 'info' | 'success' | 'error'
export interface Toast {
  id: number
  kind: ToastKind
  title: string
  lines: string[]
}

interface Timer {
  handle: ReturnType<typeof setTimeout> | null
  /** Milliseconds left on the auto-dismiss when the timer was paused. */
  remaining: number
  startedAt: number
}

let nextId = 1

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])
  const timers = new Map<number, Timer>()

  function start(id: number, ms: number) {
    timers.set(id, { handle: setTimeout(() => dismiss(id), ms), remaining: ms, startedAt: Date.now() })
  }

  function push(kind: ToastKind, title: string, lines: string[] = [], ttl = 5000) {
    const toast: Toast = { id: nextId++, kind, title, lines }
    toasts.value.push(toast)
    if (ttl > 0) start(toast.id, ttl)
    return toast.id
  }

  function dismiss(id: number) {
    const timer = timers.get(id)
    if (timer?.handle) clearTimeout(timer.handle)
    timers.delete(id)
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  /** Drops every toast, e.g. when the session ends so nothing from it lingers on the login page. */
  function clear() {
    for (const timer of timers.values()) if (timer.handle) clearTimeout(timer.handle)
    timers.clear()
    toasts.value = []
  }

  /** Stops the auto-dismiss while the pointer or focus is on the toast. */
  function pause(id: number) {
    const timer = timers.get(id)
    if (!timer?.handle) return
    clearTimeout(timer.handle)
    timer.handle = null
    timer.remaining = Math.max(0, timer.remaining - (Date.now() - timer.startedAt))
  }

  function resume(id: number) {
    const timer = timers.get(id)
    if (!timer || timer.handle) return
    // At least a second, so a toast never vanishes the moment the pointer leaves it.
    start(id, Math.max(1000, timer.remaining))
  }

  return {
    toasts,
    push,
    dismiss,
    clear,
    pause,
    resume,
    success: (title: string, lines?: string[]) => push('success', title, lines),
    error: (title: string, lines?: string[]) => push('error', title, lines, 8000),
    info: (title: string, lines?: string[]) => push('info', title, lines),
  }
})
