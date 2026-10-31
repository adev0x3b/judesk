<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { setupApi } from '@/api'
import { ApiError } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

const form = reactive({ code: '', displayName: '', email: '', password: '' })
const errors = ref<Record<string, string[]>>({})
const error = ref<string | null>(null)
const busy = ref(false)

function fieldError(field: string) {
  return errors.value[field]?.[0]
}

async function submit() {
  error.value = null
  errors.value = {}
  busy.value = true
  try {
    const admin = await setupApi.complete({ ...form })
    auth.completedSetup(admin)
    await router.push({ name: 'schedule' })
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) errors.value = e.errors
    else if (e instanceof ApiError && e.status === 409) {
      auth.needsSetup = false
      await router.push({ name: 'login' })
    } else
      error.value =
        e instanceof ApiError ? (e.status === 429 ? 'Too many attempts, wait a minute.' : e.title) : 'Could not reach the server.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="setup">
    <form class="card setup-card" @submit.prevent="submit">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true"></span>
        <h1>JuDesk</h1>
      </div>
      <h2>Create the admin account</h2>
      <p class="muted intro">
        This app has no accounts yet. The API printed a one-time setup code when it started (look at
        <code>docker compose logs api</code>). Enter it with the details of the first admin. The code changes on every restart and
        burns after a few wrong guesses.
      </p>

      <div class="field">
        <label for="code">Setup code</label>
        <input
          id="code"
          v-model="form.code"
          class="input code"
          placeholder="XXXX-XXXX-XXXX"
          autocomplete="off"
          spellcheck="false"
          required
          :aria-invalid="!!fieldError('code')"
        />
        <span v-if="fieldError('code')" class="field-error">{{ fieldError('code') }}</span>
      </div>
      <div class="field">
        <label for="displayName">Your name</label>
        <input
          id="displayName"
          v-model="form.displayName"
          class="input"
          required
          maxlength="120"
          :aria-invalid="!!fieldError('displayName')"
        />
        <span v-if="fieldError('displayName')" class="field-error">{{ fieldError('displayName') }}</span>
      </div>
      <div class="field">
        <label for="email">Email</label>
        <input
          id="email"
          v-model="form.email"
          class="input"
          type="email"
          autocomplete="username"
          required
          :aria-invalid="!!fieldError('email')"
        />
        <span v-if="fieldError('email')" class="field-error">{{ fieldError('email') }}</span>
      </div>
      <div class="field">
        <label for="password">Password</label>
        <input
          id="password"
          v-model="form.password"
          class="input"
          type="password"
          autocomplete="new-password"
          minlength="8"
          required
          :aria-invalid="!!fieldError('password')"
        />
        <span v-if="fieldError('password')" class="field-error">{{ fieldError('password') }}</span>
      </div>

      <p v-if="error" class="field-error" role="alert">{{ error }}</p>

      <button class="btn btn-primary submit" type="submit" :disabled="busy">
        {{ busy ? 'Creating…' : 'Create admin and sign in' }}
      </button>
      <p class="faint hint">Lost the code? <code>scripts/regen-setup-code.sh</code> prints a new one.</p>
    </form>
  </div>
</template>

<style scoped>
.setup {
  min-height: 100%;
  display: grid;
  place-items: center;
  padding: 24px;
  background: radial-gradient(60% 60% at 20% 0%, var(--accent-soft), transparent 60%), var(--bg);
}

.setup-card {
  width: min(420px, 100%);
  padding: 28px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
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

.intro {
  margin: 6px 0 18px;
}

code {
  font-family: var(--mono);
  font-size: 12.5px;
  padding: 1px 4px;
  border-radius: 4px;
  background: var(--surface-2);
}

.code {
  font-family: var(--mono);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.submit {
  width: 100%;
  justify-content: center;
  margin-top: 6px;
}

.hint {
  margin-top: 12px;
  text-align: center;
  font-size: 12.5px;
}
</style>
