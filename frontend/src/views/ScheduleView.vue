<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { sitesApi, skillsApi } from '@/api'
import { ApiError } from '@/api/client'
import type { Shift, ShiftRequest, Site, Skill } from '@/api/types'
import AutoAssignDialog from '@/components/AutoAssignDialog.vue'
import ShiftDrawer from '@/components/ShiftDrawer.vue'
import ShiftForm from '@/components/ShiftForm.vue'
import TechnicianPanel from '@/components/TechnicianPanel.vue'
import WeekGrid from '@/components/WeekGrid.vue'
import { dateOf, formatRange, mondayOf } from '@/lib/week'
import { useConfigStore } from '@/stores/config'
import { useScheduleStore } from '@/stores/schedule'
import { useApiErrors } from '@/composables/useApiErrors'
import { useToastStore } from '@/stores/toast'

const schedule = useScheduleStore()
const config = useConfigStore()
const toast = useToastStore()
const { fail } = useApiErrors()

const sites = ref<Site[]>([])
const skills = ref<Skill[]>([])
const form = ref<{ shift?: Shift; day?: string } | null>(null)
const formRef = ref<InstanceType<typeof ShiftForm> | null>(null)
const showAuto = ref(false)
/** True while the shift form's request is in flight, so it cannot be submitted twice. */
const saving = ref(false)
/** True while a drawer or "undo auto" action is in flight, so the buttons cannot fire twice. */
const pending = ref(false)

const dragging = computed(() => schedule.week?.technicians.find((t) => t.id === schedule.draggingTechnicianId) ?? null)

onMounted(async () => {
  try {
    const [, s, k] = await Promise.all([schedule.load(), sitesApi.list(), skillsApi.list()])
    sites.value = s
    skills.value = k
  } catch (e) {
    fail(e)
  }
})

async function onDrop(shiftId: string, technicianId: string) {
  schedule.draggingTechnicianId = null
  await assign(shiftId, technicianId, false)
}

async function assign(shiftId: string, technicianId: string, overrideHours: boolean) {
  pending.value = true
  try {
    const rejected = await schedule.assign(shiftId, technicianId, overrideHours)
    if (rejected) {
      const onlyHours = rejected.violations.length === 1 && rejected.violations[0]?.code === 'MaxHoursExceeded'
      toast.error(
        rejected.title,
        rejected.violations.map((v) => v.message).concat(onlyHours ? ['Open the shift to override the weekly limit.'] : []),
      )
    }
  } catch (e) {
    fail(e)
  } finally {
    pending.value = false
  }
}

async function unassign(assignmentId: string) {
  if (!schedule.selectedShift) return
  pending.value = true
  try {
    await schedule.unassign(schedule.selectedShift.id, assignmentId)
  } catch (e) {
    fail(e)
  } finally {
    pending.value = false
  }
}

async function saveShift(request: ShiftRequest, id?: string) {
  saving.value = true
  try {
    if (id) {
      await schedule.updateShift(id, request)
      toast.success('Shift updated')
    } else {
      const created = await schedule.createShift(request)
      // A shift created on a day outside the visible week would otherwise vanish; follow it.
      if (!schedule.days.includes(dateOf(created.startsAt, config.timeZone))) {
        await schedule.goTo(mondayOf(new Date(created.startsAt), config.timeZone))
      }
      schedule.select(created.id)
      toast.success('Shift created')
    }
    form.value = null
  } catch (e) {
    if (e instanceof ApiError && (e.status === 400 || e.status === 409)) formRef.value?.showErrors(e)
    else fail(e)
  } finally {
    saving.value = false
  }
}

async function removeShift() {
  const shift = schedule.selectedShift
  if (!shift || !window.confirm(`Delete the ${shift.siteName} shift and its assignments?`)) return
  pending.value = true
  try {
    await schedule.deleteShift(shift.id)
    toast.info('Shift deleted')
  } catch (e) {
    fail(e)
  } finally {
    pending.value = false
  }
}

async function applyAuto() {
  showAuto.value = false
  try {
    const result = await schedule.autoAssign(false)
    toast.success(
      `${result.created} assignment${result.created === 1 ? '' : 's'} made`,
      result.unfilled.length ? [`${result.unfilled.length} slot(s) could not be filled.`] : [],
    )
  } catch (e) {
    fail(e)
  }
}

async function undoAuto() {
  pending.value = true
  try {
    const removed = await schedule.clearAuto()
    toast.info(`${removed} auto assignment${removed === 1 ? '' : 's'} removed`)
  } catch (e) {
    fail(e)
  } finally {
    pending.value = false
  }
}

const hasAuto = computed(() => schedule.week?.shifts.some((s) => s.assignments.some((a) => a.source === 'Auto')) ?? false)
</script>

<template>
  <div class="schedule">
    <header class="toolbar">
      <div class="row">
        <button class="btn btn-icon" aria-label="Previous week" :disabled="schedule.loading" @click="schedule.previousWeek()">
          ‹
        </button>
        <button class="btn" :disabled="schedule.loading" @click="schedule.thisWeek()">Today</button>
        <button class="btn btn-icon" aria-label="Next week" :disabled="schedule.loading" @click="schedule.nextWeek()">›</button>
        <h1 v-if="schedule.week" class="range">{{ formatRange(schedule.week.weekStart, schedule.week.weekEnd) }}</h1>
        <span v-if="schedule.week" class="chip" :class="schedule.openSlots ? 'chip-warning' : 'chip-success'">
          {{ schedule.openSlots ? `${schedule.openSlots} open` : 'All covered' }}
        </span>
      </div>
      <div class="row">
        <select v-model="schedule.siteFilter" class="input site-filter" aria-label="Filter by site">
          <option :value="null">All sites</option>
          <option v-for="site in sites" :key="site.id" :value="site.id">{{ site.name }}</option>
        </select>
        <button class="btn" @click="form = { day: schedule.days[0] }">+ Shift</button>
        <button v-if="hasAuto" class="btn" :disabled="pending" @click="undoAuto">Undo auto</button>
        <button class="btn btn-primary" :disabled="!schedule.openSlots" @click="showAuto = true">Auto-assign</button>
        <a class="btn" :href="schedule.exportUrl" download>Export CSV</a>
      </div>
    </header>

    <div class="layout" :class="{ 'with-drawer': schedule.selectedShift }">
      <TechnicianPanel
        v-if="schedule.week"
        :technicians="schedule.week.technicians"
        @dragstart="(id) => (schedule.draggingTechnicianId = id)"
        @dragend="schedule.draggingTechnicianId = null"
      />
      <div class="grid-scroll">
        <WeekGrid
          :days="schedule.days"
          :shifts-by-day="schedule.shiftsByDay"
          :selected-shift-id="schedule.selectedShiftId"
          :dragging="dragging"
          :loading="schedule.loading && !schedule.week"
          @select="(id) => schedule.select(schedule.selectedShiftId === id ? null : id)"
          @drop="onDrop"
          @add="(day) => (form = { day })"
        />
      </div>
      <ShiftDrawer
        v-if="schedule.selectedShift"
        :shift="schedule.selectedShift"
        :pending="pending"
        @close="schedule.select(null)"
        @assign="(tech, override) => assign(schedule.selectedShift!.id, tech, override)"
        @unassign="unassign"
        @edit="form = { shift: schedule.selectedShift! }"
        @remove="removeShift"
      />
    </div>

    <ShiftForm
      v-if="form"
      ref="formRef"
      :sites="sites"
      :skills="skills"
      :shift="form.shift"
      :day="form.day"
      :saving="saving"
      @close="form = null"
      @save="saveShift"
    />
    <AutoAssignDialog
      v-if="showAuto && schedule.week"
      :shifts="schedule.week.shifts"
      :preview="() => schedule.autoAssign(true)"
      @close="showAuto = false"
      @apply="applyAuto"
    />
  </div>
</template>

<style scoped>
.schedule {
  padding: 16px 20px 32px;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 14px;
}

.range {
  margin-left: 6px;
  font-variant-numeric: tabular-nums;
}

.site-filter {
  width: auto;
}

.layout {
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.layout.with-drawer {
  grid-template-columns: 240px minmax(0, 1fr) 320px;
}

.grid-scroll {
  overflow-x: auto;
  padding-bottom: 6px;
}

@media (max-width: 1000px) {
  .layout,
  .layout.with-drawer {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .toolbar .row {
    flex-wrap: wrap;
  }

  .range {
    font-size: 18px;
  }

  .chip {
    white-space: nowrap;
  }
}
</style>
