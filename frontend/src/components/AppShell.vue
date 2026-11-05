<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { demoApi } from '@/api'
import { useApiErrors } from '@/composables/useApiErrors'
import { timeOf } from '@/lib/week'
import { useAuthStore } from '@/stores/auth'
import { useConfigStore } from '@/stores/config'
import { useScheduleStore } from '@/stores/schedule'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
import ThemeButton from '@/components/ThemeButton.vue'

const auth = useAuthStore()
const config = useConfigStore()
const schedule = useScheduleStore()
const router = useRouter()
const { fail } = useApiErrors()

/** Shown in the installation's time zone, like every other time in the app. */
const nextReset = computed(() => {
  const at = auth.demo.nextResetAt
  return at ? timeOf(at, config.timeZone) : null
})

/** The change-password dialog; the shared demo account has no password of its own to change. */
const changingPassword = ref(false)

let resetTimer: number | undefined

/** Asks the API for the next reset once the announced one has passed, and reloads the week the reset wiped. */
async function refreshDemo() {
  const previous = auth.demo.nextResetAt
  try {
    auth.demo = await demoApi.status()
    if (auth.demo.nextResetAt !== previous) schedule.load().catch(fail)
  } catch {
    // Mid-reset the API may be busy; the timer below asks again shortly.
  }
  armResetTimer()
}

function armResetTimer() {
  window.clearTimeout(resetTimer)
  const at = auth.demo.nextResetAt
  if (!auth.demo.enabled || !at) return
  const wait = new Date(at).getTime() - Date.now()
  // A little slack so the reset has run by the time we ask; once it is overdue, poll until the API reports the next one.
  resetTimer = window.setTimeout(refreshDemo, wait > 0 ? Math.min(wait + 2000, 2 ** 31 - 1) : 15000)
}

watch(() => auth.demo.nextResetAt, armResetTimer, { immediate: true })
onBeforeUnmount(() => window.clearTimeout(resetTimer))

async function logout() {
  await auth.logout()
  await router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <RouterLink to="/" class="brand">
        <span class="brand-mark" aria-hidden="true"></span>
        JuDesk
      </RouterLink>
      <nav class="nav">
        <RouterLink :to="{ name: 'schedule' }">Schedule</RouterLink>
        <RouterLink :to="{ name: 'technicians' }">Technicians</RouterLink>
        <RouterLink :to="{ name: 'sites' }">Sites</RouterLink>
        <RouterLink v-if="auth.isAdmin" :to="{ name: 'users' }">Users</RouterLink>
      </nav>
      <div class="user">
        <span class="muted">{{ auth.user?.displayName }}</span>
        <span class="chip">{{ auth.user?.role }}</span>
        <ThemeButton />
        <button v-if="!auth.user?.isDemo" class="btn btn-ghost btn-sm" @click="changingPassword = true">Change password</button>
        <button class="btn btn-ghost btn-sm" @click="logout">Sign out</button>
      </div>
    </header>
    <p v-if="auth.demo.enabled" class="demo-banner" role="status">
      Public demo: anything you change is thrown away every
      {{ auth.demo.resetMinutes === 60 ? 'hour' : `${auth.demo.resetMinutes} minutes`
      }}<template v-if="nextReset"> (next reset at {{ nextReset }})</template>.
    </p>
    <main class="content">
      <slot />
    </main>
    <ChangePasswordDialog v-if="changingPassword" @close="changingPassword = false" />
  </div>
</template>

<style scoped>
.shell {
  min-height: 100%;
  display: flex;
  flex-direction: column;
}

.topbar {
  height: var(--topbar-h);
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 0 20px;
  background: var(--surface);
  border-bottom: 1px solid var(--border);
  position: sticky;
  top: 0;
  z-index: 20;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 750;
  font-size: 17px;
  color: var(--text);
  letter-spacing: -0.02em;
}

.brand-mark {
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--accent);
  background-image: linear-gradient(transparent 30%, #fff 30% 45%, transparent 45% 58%, #fff 58% 73%, transparent 73%);
  background-size: 60% 100%;
  background-repeat: no-repeat;
  background-position: center;
}

.nav {
  display: flex;
  gap: 4px;
}

.nav a {
  padding: 6px 10px;
  border-radius: var(--radius-sm);
  color: var(--text-2);
  font-weight: 550;
}

.nav a:hover {
  background: var(--surface-2);
}

.nav a.router-link-active {
  color: var(--accent);
  background: var(--accent-soft);
}

.user {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 10px;
}

.demo-banner {
  margin: 0;
  padding: 6px 20px;
  background: var(--warning-soft);
  color: var(--warning);
  font-size: 13px;
  text-align: center;
  border-bottom: 1px solid var(--border);
}

.content {
  flex: 1;
  min-height: 0;
}

@media (max-width: 720px) {
  .topbar {
    height: auto;
    min-height: var(--topbar-h);
    flex-wrap: wrap;
    gap: 8px 12px;
    padding: 8px 12px;
  }

  .nav {
    order: 3;
    width: 100%;
    overflow-x: auto;
  }

  .demo-banner {
    padding: 6px 12px;
  }

  .user .muted {
    display: none;
  }

  /* Two text buttons plus the chip may not fit beside the brand; let them wrap onto their own line. */
  .user {
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 6px;
  }
}
</style>
