<script setup lang="ts">
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

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
        <button class="btn btn-ghost btn-sm" @click="logout">Sign out</button>
      </div>
    </header>
    <main class="content">
      <slot />
    </main>
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

.content {
  flex: 1;
  min-height: 0;
}

@media (max-width: 720px) {
  .topbar {
    gap: 12px;
    padding: 0 12px;
  }

  .user .muted {
    display: none;
  }
}
</style>
