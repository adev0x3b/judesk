<script setup lang="ts">
import { ref, watch } from 'vue'
import type { Candidate, Shift } from '@/api/types'
import { shiftsApi } from '@/api'
import { formatDay, dateOf, hoursLabel, timeOf } from '@/lib/week'
import { useConfigStore } from '@/stores/config'

const props = defineProps<{
  shift: Shift
  /** True while the parent is waiting on the API for one of this drawer's actions. */
  pending?: boolean
}>()
const emit = defineEmits<{
  close: []
  assign: [technicianId: string, overrideHours: boolean]
  unassign: [assignmentId: string]
  edit: []
  remove: []
}>()

const config = useConfigStore()
const time = (iso: string) => timeOf(iso, config.timeZone)

const candidates = ref<Candidate[]>([])
const loading = ref(false)
const loadFailed = ref(false)
const showIneligible = ref(false)
/** Bumped on every request so a response for a shift that is no longer selected is ignored. */
let loadSeq = 0

async function loadCandidates() {
  const seq = ++loadSeq
  loading.value = true
  loadFailed.value = false
  try {
    const result = await shiftsApi.candidates(props.shift.id)
    if (seq === loadSeq) candidates.value = result
  } catch {
    if (seq === loadSeq) loadFailed.value = true
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

watch(() => [props.shift.id, props.shift.assignments.length], loadCandidates, { immediate: true })

function onlyHoursViolation(c: Candidate) {
  return c.violations.length === 1 && c.violations[0]?.code === 'MaxHoursExceeded'
}
</script>

<template>
  <aside class="drawer card" aria-label="Shift details">
    <header class="drawer-head">
      <div>
        <div class="eyebrow faint">
          {{ formatDay(dateOf(shift.startsAt, config.timeZone)) }} · {{ time(shift.startsAt) }}–{{ time(shift.endsAt) }} ·
          {{ hoursLabel(shift.hours) }}
        </div>
        <h2 :style="{ color: shift.siteColor ?? undefined }">{{ shift.siteName }}</h2>
        <div class="row meta">
          <span class="chip chip-accent">{{ shift.requiredSkillName }}</span>
          <span class="chip">{{ shift.assignments.length }} / {{ shift.headcount }} assigned</span>
        </div>
      </div>
      <button class="btn btn-ghost btn-icon" aria-label="Close" @click="emit('close')">✕</button>
    </header>

    <p v-if="shift.notes" class="notes">{{ shift.notes }}</p>

    <section>
      <h3>Assigned</h3>
      <ul v-if="shift.assignments.length" class="plain">
        <li v-for="a in shift.assignments" :key="a.id" class="row assigned">
          <span class="grow">{{ a.technicianName }}</span>
          <span v-if="a.source === 'Auto'" class="chip">auto</span>
          <button class="btn btn-sm" :disabled="pending" @click="emit('unassign', a.id)">Unassign</button>
        </li>
      </ul>
      <p v-else class="faint">Nobody yet.</p>
    </section>

    <section>
      <div class="row">
        <h3 class="grow">Candidates</h3>
        <label class="checkbox faint">
          <input v-model="showIneligible" type="checkbox" />
          show ineligible
        </label>
      </div>
      <p v-if="loading" class="faint">Checking rules…</p>
      <p v-else-if="loadFailed" class="field-error" role="alert">Could not load candidates.</p>
      <p v-else-if="shift.assignments.length >= shift.headcount" class="faint">This shift is fully staffed.</p>
      <p v-else-if="!showIneligible && !candidates.some((c) => c.eligible || onlyHoursViolation(c))" class="faint">
        Nobody is eligible right now. Tick "show ineligible" to see why.
      </p>
      <ul v-else class="plain candidates">
        <li
          v-for="c in candidates.filter((x) => showIneligible || x.eligible || onlyHoursViolation(x))"
          :key="c.technicianId"
          class="candidate"
          :class="{ ineligible: !c.eligible }"
        >
          <div class="row">
            <span class="grow name">{{ c.fullName }}</span>
            <span class="mono faint">{{ hoursLabel(c.scheduledHours) }} / {{ c.maxWeeklyHours }}</span>
            <button
              v-if="c.eligible"
              class="btn btn-sm btn-primary"
              :disabled="pending"
              @click="emit('assign', c.technicianId, false)"
            >
              Assign
            </button>
            <button
              v-else-if="onlyHoursViolation(c)"
              class="btn btn-sm"
              title="Assign anyway, over the weekly limit"
              :disabled="pending"
              @click="emit('assign', c.technicianId, true)"
            >
              Override
            </button>
          </div>
          <ul v-if="c.violations.length" class="plain violations">
            <li
              v-for="(v, i) in c.violations"
              :key="i"
              class="chip"
              :class="v.code === 'MaxHoursExceeded' ? 'chip-warning' : 'chip-danger'"
            >
              {{ v.message }}
            </li>
          </ul>
        </li>
      </ul>
    </section>

    <footer class="row drawer-foot">
      <button class="btn" @click="emit('edit')">Edit</button>
      <button class="btn btn-danger" :disabled="pending" @click="emit('remove')">Delete shift</button>
    </footer>
  </aside>
</template>

<style scoped>
.drawer {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 16px;
  position: sticky;
  top: calc(var(--topbar-h) + 16px);
  max-height: calc(100vh - var(--topbar-h) - 32px);
  overflow-y: auto;
}

.drawer-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
}

.eyebrow {
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}

.meta {
  margin-top: 6px;
}

.notes {
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--text-2);
}

section h3 {
  margin-bottom: 8px;
}

.plain {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.assigned {
  padding: 6px 8px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}

.candidate {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.candidate.ineligible .name {
  color: var(--text-2);
}

.violations {
  flex-direction: row;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 6px;
}

.violations .chip {
  white-space: normal;
}

.drawer-foot {
  justify-content: space-between;
  padding-top: 8px;
  border-top: 1px solid var(--border);
}

/* Narrow screens: a bottom sheet over the grid, so tapping a shift shows something right away. */
@media (max-width: 1000px) {
  .drawer {
    position: fixed;
    inset: auto 0 0 0;
    top: auto;
    z-index: 30;
    max-height: 70vh;
    border-radius: 14px 14px 0 0;
    box-shadow: var(--shadow-lg);
  }
}
</style>
