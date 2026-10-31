<script setup lang="ts">
import { computed } from 'vue'
import { THEME_LABELS, useTheme } from '@/lib/theme'

const { theme, cycle } = useTheme()
const label = computed(() => THEME_LABELS[theme.value])
</script>

<template>
  <button
    class="btn btn-ghost btn-icon"
    type="button"
    :title="`${label} (click to change)`"
    :aria-label="`${label}, click to change`"
    @click="cycle"
  >
    <svg v-if="theme === 'light'" class="icon" viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="4.5" />
      <path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1" />
    </svg>
    <svg v-else-if="theme === 'dark'" class="icon" viewBox="0 0 24 24" aria-hidden="true">
      <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z" />
    </svg>
    <svg v-else class="icon" viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 3.5v17A8.5 8.5 0 0 0 12 3.5Z" fill="currentColor" stroke="none" />
    </svg>
  </button>
</template>

<style scoped>
.icon {
  width: 18px;
  height: 18px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}
</style>
