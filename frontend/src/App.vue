<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/AppShell.vue'
import ToastStack from '@/components/ToastStack.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

// The shell follows the route, not the session: on sign-out the page would otherwise be
// remounted outside the shell and fire its requests again with no cookie.
const inShell = computed(() => route.matched.length > 0 && !route.meta.public)

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
  <AppShell v-if="inShell">
    <RouterView />
  </AppShell>
  <RouterView v-else />
  <ToastStack />
</template>
