<script setup lang="ts">
import { onMounted, onUnmounted, ref, useId } from 'vue'

defineProps<{ title: string }>()
const emit = defineEmits<{ close: [] }>()

const dialog = ref<HTMLDialogElement | null>(null)
const titleId = useId()
// The element that opened the dialog gets focus back once the dialog is gone.
const previouslyFocused = document.activeElement as HTMLElement | null

onMounted(() => dialog.value?.showModal())
onUnmounted(() => previouslyFocused?.focus())
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="modal"
      :aria-labelledby="titleId"
      @click.self="emit('close')"
      @cancel.prevent="emit('close')"
      @close="emit('close')"
    >
      <div class="modal-body">
        <div class="modal-header">
          <h2 :id="titleId">{{ title }}</h2>
          <button class="btn btn-ghost btn-icon" aria-label="Close" @click="emit('close')">✕</button>
        </div>
        <slot />
      </div>
    </dialog>
  </Teleport>
</template>
