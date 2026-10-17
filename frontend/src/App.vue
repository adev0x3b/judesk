<script setup lang="ts">
import { watch } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import AppShell from '@/components/AppShell.vue'
import ToastStack from '@/components/ToastStack.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

// A session the API no longer accepts sends the user back to the login page with a reason,
// instead of leaving a logged-out page half rendered.
watch(
  () => auth.expired,
  (expired) => {
    if (expired) router.push({ name: 'login', query: { expired: '1' } })
  },
)
</script>

<template>
  <AppShell v-if="auth.isAuthenticated">
    <RouterView />
  </AppShell>
  <RouterView v-else />
  <ToastStack />
</template>
