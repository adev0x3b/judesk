<script setup lang="ts">
defineProps<{ title: string }>()
const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <Teleport to="body">
    <div class="modal-backdrop" @click.self="emit('close')" @keydown.esc="emit('close')">
      <div class="modal" role="dialog" aria-modal="true" :aria-label="title">
        <div class="modal-header">
          <h2>{{ title }}</h2>
          <button class="btn btn-ghost btn-icon" aria-label="Close" @click="emit('close')">✕</button>
        </div>
        <slot />
      </div>
    </div>
  </Teleport>
</template>
