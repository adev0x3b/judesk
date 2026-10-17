<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { AutoAssignResult, Shift } from '@/api/types'
import { formatDay, dateOf, timeOf } from '@/lib/week'
import ModalDialog from './ModalDialog.vue'

const props = defineProps<{
  shifts: Shift[]
  preview: () => Promise<AutoAssignResult>
}>()
const emit = defineEmits<{ close: []; apply: [] }>()

const result = ref<AutoAssignResult | null>(null)
const error = ref<string | null>(null)

function describe(shiftId: string) {
  const s = props.shifts.find((x) => x.id === shiftId)
  return s ? `${formatDay(dateOf(s.startsAt))} ${timeOf(s.startsAt)} · ${s.siteName} · ${s.requiredSkillName}` : shiftId
}

onMounted(async () => {
  try {
    result.value = await props.preview()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Preview failed.'
  }
})
</script>

<template>
  <ModalDialog title="Auto-assign this week" @close="emit('close')">
    <p class="muted intro">
      Open slots go to the qualified technician with the fewest hours this week, most constrained shifts first. Nothing breaks a
      rule; you can undo it afterwards.
    </p>
    <p v-if="error" class="field-error" role="alert">{{ error }}</p>
    <p v-else-if="!result" class="faint">Planning…</p>
    <template v-else>
      <h3>{{ result.assignments.length }} assignment{{ result.assignments.length === 1 ? '' : 's' }} proposed</h3>
      <ul class="list">
        <li v-for="(a, i) in result.assignments" :key="i">
          <strong>{{ a.technicianName }}</strong> <span class="faint">→</span> {{ describe(a.shiftId) }}
        </li>
      </ul>
      <template v-if="result.unfilled.length">
        <h3 class="unfilled-title">{{ result.unfilled.length }} slot{{ result.unfilled.length === 1 ? '' : 's' }} left open</h3>
        <ul class="list">
          <li v-for="(u, i) in result.unfilled" :key="i">
            {{ describe(u.shiftId) }} <span class="chip chip-warning">{{ u.missing }} missing</span>
            <div class="faint">{{ u.reason }}</div>
          </li>
        </ul>
      </template>
    </template>
    <div class="modal-actions">
      <button class="btn" @click="emit('close')">Cancel</button>
      <button class="btn btn-primary" :disabled="!result || result.assignments.length === 0" @click="emit('apply')">Apply</button>
    </div>
  </ModalDialog>
</template>

<style scoped>
.intro {
  margin-bottom: 12px;
}

.list {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 40vh;
  overflow-y: auto;
}

.list li {
  padding: 6px 8px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}

.unfilled-title {
  margin-top: 14px;
}
</style>
