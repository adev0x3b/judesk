import { defineStore } from 'pinia'
import { ref } from 'vue'
import { configApi } from '@/api'
import { isValidTimeZone } from '@/lib/week'

export const DEFAULT_TIME_ZONE = 'UTC'
export const DEFAULT_PASSWORD_MIN_LENGTH = 12

/**
 * Public installation settings, fetched once at start (no login needed). When the call fails the
 * defaults stay in place, so the app still works and shows UTC rather than nothing.
 */
export const useConfigStore = defineStore('config', () => {
  /** IANA time zone every wall-clock time is shown and typed in. */
  const timeZone = ref(DEFAULT_TIME_ZONE)
  const passwordMinLength = ref(DEFAULT_PASSWORD_MIN_LENGTH)
  const loaded = ref(false)

  async function load() {
    if (loaded.value) return
    try {
      const config = await configApi.get()
      timeZone.value = isValidTimeZone(config.timeZone) ? config.timeZone : DEFAULT_TIME_ZONE
      passwordMinLength.value =
        Number.isInteger(config.passwordMinLength) && config.passwordMinLength > 0
          ? config.passwordMinLength
          : DEFAULT_PASSWORD_MIN_LENGTH
      loaded.value = true
    } catch {
      // The defaults stand; the next restore() tries again.
    }
  }

  return { timeZone, passwordMinLength, loaded, load }
})
