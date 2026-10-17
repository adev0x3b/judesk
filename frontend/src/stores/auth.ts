import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/api'
import { ApiError, setUnauthorizedHandler } from '@/api/client'
import type { User } from '@/api/types'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const checked = ref(false)

  const isAuthenticated = computed(() => user.value !== null)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  /** Restores the session from the cookie on first load. */
  async function restore() {
    if (checked.value) return
    try {
      user.value = await authApi.me()
    } catch (error) {
      if (!(error instanceof ApiError && error.isUnauthorized)) throw error
      user.value = null
    } finally {
      checked.value = true
    }
  }

  async function login(email: string, password: string) {
    user.value = await authApi.login(email, password)
    checked.value = true
  }

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      user.value = null
    }
  }

  setUnauthorizedHandler(() => {
    user.value = null
  })

  return { user, checked, isAuthenticated, isAdmin, restore, login, logout }
})
