<script setup lang="ts">
import { computed, ref } from 'vue'
import type { TechnicianLoad } from '@/api/types'
import { hoursLabel } from '@/lib/week'

const props = defineProps<{ technicians: TechnicianLoad[] }>()
const emit = defineEmits<{
  dragstart: [technicianId: string]
  dragend: []
}>()

const query = ref('')
const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  return props.technicians.filter(
    (t) => q === '' || t.fullName.toLowerCase().includes(q) || t.skills.some((s) => s.toLowerCase().includes(q)),
  )
})

function load(t: TechnicianLoad) {
  return Math.min(100, Math.round((t.scheduledHours / t.maxWeeklyHours) * 100))
}

function level(t: TechnicianLoad) {
  const ratio = t.scheduledHours / t.maxWeeklyHours
  return ratio > 1 ? 'over' : ratio >= 0.9 ? 'high' : 'normal'
}

function onDragStart(event: DragEvent, t: TechnicianLoad) {
  event.dataTransfer?.setData('application/x-judesk-technician', t.id)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
  emit('dragstart', t.id)
}
</script>

<template>
  <aside class="panel card">
    <header class="panel-head">
      <h2>Technicians</h2>
      <span class="faint">{{ technicians.length }}</span>
    </header>
    <input
      v-model="query"
      class="input search"
      type="search"
      placeholder="Search name or skill"
      aria-label="Search technicians"
    />
    <p class="help faint">Drag a technician onto a shift to assign.</p>
    <ul class="list">
      <li
        v-for="t in filtered"
        :key="t.id"
        class="tech"
        :class="{ inactive: !t.active }"
        :draggable="t.active"
        @dragstart="onDragStart($event, t)"
        @dragend="emit('dragend')"
      >
        <div class="tech-row">
          <span class="tech-name">{{ t.fullName }}</span>
          <span class="tech-hours mono" :class="level(t)">{{ hoursLabel(t.scheduledHours) }} / {{ t.maxWeeklyHours }}</span>
        </div>
        <div class="bar" aria-hidden="true">
          <div class="bar-fill" :class="level(t)" :style="{ width: `${load(t)}%` }"></div>
        </div>
        <div class="skills">
          <span v-for="skill in t.skills" :key="skill" class="chip">{{ skill }}</span>
          <span v-if="!t.active" class="chip chip-danger">Inactive</span>
        </div>
      </li>
    </ul>
  </aside>
</template>

<style scoped>
.panel {
  display: flex;
  flex-direction: column;
  padding: 12px;
  gap: 8px;
  max-height: calc(100vh - var(--topbar-h) - 120px);
  position: sticky;
  top: calc(var(--topbar-h) + 16px);
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.help {
  font-size: 12px;
}

.list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
  overflow-y: auto;
}

.tech {
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  cursor: grab;
}

.tech:active {
  cursor: grabbing;
}

.tech.inactive {
  cursor: not-allowed;
  opacity: 0.6;
}

.tech-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
}

.tech-name {
  font-weight: 600;
}

.tech-hours {
  color: var(--text-2);
  white-space: nowrap;
}

.tech-hours.high {
  color: var(--warning);
}

.tech-hours.over {
  color: var(--danger);
}

.bar {
  height: 4px;
  margin: 6px 0;
  border-radius: 2px;
  background: var(--surface-2);
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  background: var(--accent);
  transition: width 0.2s;
}

.bar-fill.high {
  background: var(--warning);
}

.bar-fill.over {
  background: var(--danger);
}

.skills {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

@media (max-width: 1000px) {
  .panel {
    position: static;
    max-height: none;
  }
}
</style>
