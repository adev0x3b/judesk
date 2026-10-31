<script setup lang="ts">
import type { Shift, TechnicianLoad } from '@/api/types'
import { formatDay, isoDate } from '@/lib/week'
import ShiftCard from './ShiftCard.vue'

defineProps<{
  days: string[]
  shiftsByDay: Map<string, Shift[]>
  selectedShiftId: string | null
  dragging: TechnicianLoad | null
  /** True while the first week is still on its way: the columns say so instead of "No shifts". */
  loading?: boolean
}>()

const emit = defineEmits<{
  select: [shiftId: string]
  drop: [shiftId: string, technicianId: string]
  add: [day: string]
}>()

const today = isoDate(new Date())
</script>

<template>
  <div class="grid" role="list">
    <section v-for="day in days" :key="day" class="day" :class="{ today: day === today }" role="listitem">
      <header class="day-head">
        <span class="day-name">{{ formatDay(day) }}</span>
        <button class="btn btn-ghost btn-sm add" :aria-label="`Add shift on ${formatDay(day)}`" @click="emit('add', day)">
          +
        </button>
      </header>
      <div class="day-body">
        <ShiftCard
          v-for="shift in shiftsByDay.get(day) ?? []"
          :key="shift.id"
          :shift="shift"
          :selected="shift.id === selectedShiftId"
          :dragging="dragging"
          @select="(id) => emit('select', id)"
          @drop="(id, tech) => emit('drop', id, tech)"
        />
        <p v-if="loading" class="none faint">Loading…</p>
        <p v-else-if="(shiftsByDay.get(day) ?? []).length === 0" class="none faint">No shifts</p>
      </div>
    </section>
  </div>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(150px, 1fr));
  gap: 8px;
  min-width: 1100px;
}

.day {
  display: flex;
  flex-direction: column;
  min-height: 300px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius);
}

.day.today {
  border-color: var(--accent);
}

.day-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 6px 8px 12px;
  font-weight: 650;
  border-bottom: 1px solid var(--border);
}

.today .day-name {
  color: var(--accent);
}

.add {
  width: 26px;
  height: 26px;
  justify-content: center;
  padding: 0;
  font-size: 16px;
}

.day-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px;
}

.none {
  text-align: center;
  padding: 16px 0;
  font-size: 12.5px;
}
</style>
