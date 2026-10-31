<script setup lang="ts">
import { useToastStore } from '@/stores/toast'

const toasts = useToastStore()
</script>

<template>
  <div class="toasts" aria-live="polite">
    <TransitionGroup name="toast">
      <div
        v-for="toast in toasts.toasts"
        :key="toast.id"
        class="toast"
        :class="`toast-${toast.kind}`"
        :role="toast.kind === 'error' ? 'alert' : undefined"
        @mouseenter="toasts.pause(toast.id)"
        @mouseleave="toasts.resume(toast.id)"
        @focusin="toasts.pause(toast.id)"
        @focusout="toasts.resume(toast.id)"
      >
        <div class="toast-body">
          <strong>{{ toast.title }}</strong>
          <p v-for="(line, i) in toast.lines" :key="i">{{ line }}</p>
        </div>
        <button class="btn btn-ghost btn-sm" aria-label="Dismiss" @click="toasts.dismiss(toast.id)">✕</button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toasts {
  position: fixed;
  right: 16px;
  bottom: 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  z-index: 100;
  width: min(380px, calc(100vw - 32px));
}

.toast {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 12px 10px 12px 14px;
  border-radius: var(--radius);
  background: var(--surface);
  border: 1px solid var(--border);
  border-left: 4px solid var(--accent);
  box-shadow: var(--shadow-lg);
}

.toast-success {
  border-left-color: var(--success);
}

.toast-error {
  border-left-color: var(--danger);
}

.toast-body {
  flex: 1;
}

.toast-body p {
  margin-top: 2px;
  color: var(--text-2);
  font-size: 13px;
}

.toast-enter-active,
.toast-leave-active {
  transition:
    opacity 0.18s,
    transform 0.18s;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
