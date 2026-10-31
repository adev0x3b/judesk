<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)
const expired = route.query.expired === '1'

async function tryDemo() {
  error.value = null
  busy.value = true
  try {
    await auth.loginDemo()
    await router.push('/')
  } catch (e) {
    error.value =
      e instanceof ApiError ? (e.status === 429 ? 'Too many attempts, wait a minute.' : e.title) : 'Could not reach the server.'
  } finally {
    busy.value = false
  }
}

async function submit() {
  error.value = null
  busy.value = true
  try {
    await auth.login(email.value, password.value)
    const next = typeof route.query.next === 'string' ? route.query.next : '/'
    await router.push(next)
  } catch (e) {
    error.value =
      e instanceof ApiError ? (e.status === 429 ? 'Too many attempts, wait a minute.' : e.title) : 'Could not reach the server.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login">
    <form class="card login-card" @submit.prevent="submit">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true"></span>
        <h1>JuDesk</h1>
      </div>
      <p class="muted tagline">Shift scheduling for field technicians</p>

      <p v-if="expired && !error" class="notice" role="status">Your session has expired. Sign in again.</p>

      <div class="field">
        <label for="email">Email</label>
        <input id="email" v-model="email" class="input" type="email" autocomplete="username" required autofocus />
      </div>
      <div class="field">
        <label for="password">Password</label>
        <input id="password" v-model="password" class="input" type="password" autocomplete="current-password" required />
      </div>

      <p v-if="error" class="field-error" role="alert">{{ error }}</p>

      <button class="btn btn-primary submit" type="submit" :disabled="busy">
        {{ busy ? 'Signing in…' : 'Sign in' }}
      </button>

      <div v-if="auth.demo.enabled" class="demo">
        <div class="divider"><span>or</span></div>
        <button class="btn submit" type="button" :disabled="busy" @click="tryDemo">Try the demo</button>
        <p class="muted demo-hint">
          Signs you in as a planner with a sample fleet. Everything resets every
          {{ auth.demo.resetMinutes === 60 ? 'hour' : `${auth.demo.resetMinutes} minutes` }}.
        </p>
      </div>
    </form>
  </div>
</template>

<style scoped>
.login {
  min-height: 100%;
  display: grid;
  place-items: center;
  padding: 24px;
  background: radial-gradient(60% 60% at 20% 0%, var(--accent-soft), transparent 60%), var(--bg);
}

.login-card {
  width: min(380px, 100%);
  padding: 28px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
}

.brand h1 {
  font-size: 24px;
  letter-spacing: -0.02em;
}

.brand-mark {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: var(--accent);
  background-image: linear-gradient(transparent 30%, #fff 30% 45%, transparent 45% 58%, #fff 58% 73%, transparent 73%);
  background-size: 60% 100%;
  background-repeat: no-repeat;
  background-position: center;
}

.tagline {
  margin: 6px 0 22px;
}

.notice {
  margin: -8px 0 16px;
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  background: var(--warning-soft);
  color: var(--warning);
  font-size: 13px;
}

.submit {
  width: 100%;
  justify-content: center;
  margin-top: 6px;
}

.demo {
  margin-top: 18px;
}

.divider {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  color: var(--text-3);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border);
}

.demo-hint {
  margin: 10px 0 0;
  font-size: 13px;
  text-align: center;
}
</style>
