<script setup lang="ts">
import { computed } from 'vue'
import type { Shift, TechnicianLoad } from '@/api/types'
import { timeOf } from '@/lib/week'

const props = defineProps<{
  shift: Shift
  selected: boolean
  /** The technician being dragged over the grid, if any, for the eligibility hint. */
  dragging: TechnicianLoad | null
}>()

const emit = defineEmits<{
  select: [shiftId: string]
  drop: [shiftId: string, technicianId: string]
}>()

const open = computed(() => Math.max(0, props.shift.headcount - props.shift.assignments.length))
const label = computed(() => {
  const s = props.shift
  const assigned = s.assignments.length ? `assigned ${s.assignments.map((a) => a.technicianName).join(', ')}` : 'nobody assigned'
  return `${s.siteName} ${timeOf(s.startsAt)} to ${timeOf(s.endsAt)}, ${s.requiredSkillName}, ${assigned}, ${open.value} open`
})
const slots = computed(() => [
  ...props.shift.assignments.map((a) => ({ key: a.id, name: a.technicianName, source: a.source })),
  ...Array.from({ length: open.value }, (_, i) => ({ key: `open-${i}`, name: null, source: null })),
])

/** Quick client-side hint while dragging; the API has the final say. */
const hint = computed<'ok' | 'bad' | null>(() => {
  const t = props.dragging
  if (!t) return null
  if (!t.active) return 'bad'
  if (!t.skills.includes(props.shift.requiredSkillName)) return 'bad'
  if (props.shift.assignments.some((a) => a.technicianId === t.id)) return 'bad'
  if (open.value === 0) return 'bad'
  return 'ok'
})

function onDrop(event: DragEvent) {
  const technicianId = event.dataTransfer?.getData('application/x-judesk-technician')
  if (technicianId) emit('drop', props.shift.id, technicianId)
}
</script>

<template>
  <article
    class="shift"
    :class="{ selected, full: open === 0, 'hint-ok': hint === 'ok', 'hint-bad': hint === 'bad' }"
    :style="{ '--site-color': shift.siteColor ?? 'var(--text-3)' }"
    tabindex="0"
    role="button"
    :aria-label="label"
    @click="emit('select', shift.id)"
    @keydown.enter.prevent="emit('select', shift.id)"
    @keydown.space.prevent="emit('select', shift.id)"
    @dragover.prevent
    @drop.prevent="onDrop"
  >
    <header class="shift-head">
      <span class="time">{{ timeOf(shift.startsAt) }}–{{ timeOf(shift.endsAt) }}</span>
      <span class="hours faint">{{ shift.hours }} h</span>
    </header>
    <div class="site" :title="shift.siteName">{{ shift.siteName }}</div>
    <div class="skill">{{ shift.requiredSkillName }}</div>
    <ul class="slots">
      <li v-for="slot in slots" :key="slot.key" class="slot" :class="{ open: slot.name === null, auto: slot.source === 'Auto' }">
        <span v-if="slot.name" class="name">{{ slot.name }}</span>
        <span v-else class="placeholder">Open</span>
        <span v-if="slot.source === 'Auto'" class="auto-mark" title="Assigned by the auto-scheduler">A</span>
      </li>
    </ul>
  </article>
</template>

<style scoped>
.shift {
  position: relative;
  padding: 8px 10px 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  cursor: pointer;
  transition:
    box-shadow 0.12s,
    border-color 0.12s,
    transform 0.08s;
}

.shift::before {
  content: '';
  position: absolute;
  left: 0;
  top: 6px;
  bottom: 6px;
  width: 4px;
  border-radius: 0 3px 3px 0;
  background: var(--site-color);
}

.shift:hover,
.shift:focus-visible {
  border-color: var(--border-strong);
  box-shadow: var(--shadow);
  outline: none;
}

.shift.selected {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--accent-soft);
}

.shift.hint-ok {
  border-style: dashed;
  border-color: var(--success);
  background: var(--success-soft);
}

.shift.hint-bad {
  opacity: 0.45;
}

.shift-head {
  display: flex;
  justify-content: space-between;
  font-size: 12.5px;
  font-weight: 650;
  font-variant-numeric: tabular-nums;
}

.site {
  margin-top: 2px;
  font-weight: 600;
  line-height: 1.25;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.skill {
  font-size: 12px;
  color: var(--text-2);
}

.slots {
  list-style: none;
  margin: 6px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.slot {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 12.5px;
  background: var(--surface-2);
}

.slot.open {
  background: transparent;
  border: 1px dashed var(--border-strong);
  color: var(--text-3);
}

.name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.auto-mark {
  font-size: 10px;
  font-weight: 700;
  padding: 0 4px;
  border-radius: 3px;
  background: var(--accent-soft);
  color: var(--accent);
}

.full .slots .slot:not(.open) {
  background: var(--surface-2);
}
</style>
