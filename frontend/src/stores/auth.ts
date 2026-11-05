import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, demoApi, setupApi } from '@/api'
import { ApiError, setUnauthorizedHandler } from '@/api/client'
import type { DemoStatus, User } from '@/api/types'
import { useConfigStore } from '@/stores/config'
import { useScheduleStore } from '@/stores/schedule'
import { useToastStore } from '@/stores/toast'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const checked = ref(false)
  /** True while the app has no accounts: only the setup page is reachable. */
  const needsSetup = ref(false)
  /** Set when the API rejected a session the app believed was valid (expired, or the user was deleted). */
  const expired = ref(false)
  /** Set when restoring the session failed for a reason other than being signed out: the API is unreachable or starting. */
  const serverDown = ref(false)
  /** Public demo: a passwordless planner login and a data reset on a timer. */
  const demo = ref<DemoStatus>({ enabled: false, resetMinutes: 0, nextResetAt: null })

  const isAuthenticated = computed(() => user.value !== null)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  /**
   * Restores the session from the cookie on first load, after checking whether setup is still pending.
   * Leaves `checked` unset when the API could not be reached, so a later call tries again.
   * The public config (time zone, password rules) is fetched alongside; it never fails the restore.
   */
  async function restore() {
    if (checked.value) return
    try {
      const [setup, demoStatus] = await Promise.all([setupApi.status(), demoApi.status(), useConfigStore().load()])
      demo.value = demoStatus
      needsSetup.value = setup.needsSetup
      user.value = needsSetup.value ? null : await authApi.me()
    } catch (error) {
      if (!(error instanceof ApiError && error.isUnauthorized)) {
        serverDown.value = true
        return
      }
      user.value = null
    }
    serverDown.value = false
    checked.value = true
  }

  /** Setup signs the new admin in; the app is open from here on. */
  function completedSetup(admin: User) {
    user.value = admin
    needsSetup.value = false
    checked.value = true
  }

  async function login(email: string, password: string) {
    user.value = await authApi.login(email, password)
    checked.value = true
    expired.value = false
  }

  async function loginDemo() {
    user.value = await authApi.loginDemo()
    checked.value = true
    expired.value = false
  }

  /**
   * Ends the session on the server first. If that fails (API down, network error), the user stays
   * signed in and the error reaches the caller, so a shared computer is never left with a live
   * session that only looks signed out. A 401 means the session was already gone.
   */
  async function logout() {
    try {
      await authApi.logout()
    } catch (e) {
      if (!(e instanceof ApiError && e.isUnauthorized)) throw e
    }
    endSession()
  }

  setUnauthorizedHandler(() => {
    if (user.value !== null) expired.value = true
    endSession()
  })

  /** Forgets everything that belonged to the signed-in user. */
  function endSession() {
    user.value = null
    useScheduleStore().reset()
    useToastStore().clear()
  }

  return {
    user,
    checked,
    expired,
    serverDown,
    needsSetup,
    demo,
    isAuthenticated,
    isAdmin,
    restore,
    completedSetup,
    login,
    loginDemo,
    logout,
  }
})
