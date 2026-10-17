<script setup lang="ts">
import { RouterView } from 'vue-router'
import AppShell from '@/components/AppShell.vue'
import ToastStack from '@/components/ToastStack.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <AppShell v-if="auth.isAuthenticated">
    <RouterView />
  </AppShell>
  <RouterView v-else />
  <ToastStack />
</template>
