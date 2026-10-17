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

.submit {
  width: 100%;
  justify-content: center;
  margin-top: 6px;
}
</style>
