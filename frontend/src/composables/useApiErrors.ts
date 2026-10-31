import { ApiError } from '@/api/client'
import { useToastStore } from '@/stores/toast'

/** One place that turns a failed call into a toast, so every view reports errors the same way. */
export function useApiErrors() {
  const toast = useToastStore()

  function fail(e: unknown) {
    if (e instanceof ApiError) toast.error(e.title, e.detail ? [e.detail] : [])
    else toast.error('Something went wrong', import.meta.env.DEV ? [String(e)] : [])
  }

  return { fail }
}
