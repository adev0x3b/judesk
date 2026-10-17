import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ToastKind = 'info' | 'success' | 'error'
export interface Toast {
  id: number
  kind: ToastKind
  title: string
  lines: string[]
}

let nextId = 1

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])

  function push(kind: ToastKind, title: string, lines: string[] = [], ttl = 5000) {
    const toast: Toast = { id: nextId++, kind, title, lines }
    toasts.value.push(toast)
    if (ttl > 0) setTimeout(() => dismiss(toast.id), ttl)
    return toast.id
  }

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  return {
    toasts,
    push,
    dismiss,
    success: (title: string, lines?: string[]) => push('success', title, lines),
    error: (title: string, lines?: string[]) => push('error', title, lines, 8000),
    info: (title: string, lines?: string[]) => push('info', title, lines),
  }
})
