import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { scheduleApi, shiftsApi } from '@/api'
import { ApiError } from '@/api/client'
import type { AutoAssignResult, Shift, ShiftRequest, WeekSchedule } from '@/api/types'
import { addDays, isoDate, mondayOf, weekDays } from '@/lib/week'

/** The week on screen and every action that changes it. Each mutation reloads the week from the API. */
export const useScheduleStore = defineStore('schedule', () => {
  const weekStart = ref(isoDate(mondayOf(new Date())))
  const week = ref<WeekSchedule | null>(null)
  const loading = ref(false)
  const siteFilter = ref<string | null>(null)
  const selectedShiftId = ref<string | null>(null)
  const draggingTechnicianId = ref<string | null>(null)

  const days = computed(() => weekDays(weekStart.value))

  const visibleShifts = computed(() =>
    (week.value?.shifts ?? []).filter((s) => siteFilter.value === null || s.siteId === siteFilter.value),
  )

  const shiftsByDay = computed(() => {
    const map = new Map<string, Shift[]>(days.value.map((d) => [d, []]))
    for (const shift of visibleShifts.value) {
      map.get(shift.startsAt.slice(0, 10))?.push(shift)
    }
    return map
  })

  const selectedShift = computed(() => week.value?.shifts.find((s) => s.id === selectedShiftId.value) ?? null)

  const openSlots = computed(() => visibleShifts.value.reduce((n, s) => n + Math.max(0, s.headcount - s.assignments.length), 0))

  async function load() {
    loading.value = true
    try {
      week.value = await scheduleApi.week(weekStart.value)
    } finally {
      loading.value = false
    }
  }

  function goTo(start: string) {
    weekStart.value = start
    return load()
  }

  const nextWeek = () => goTo(isoDate(addDays(new Date(`${weekStart.value}T00:00:00Z`), 7)))
  const previousWeek = () => goTo(isoDate(addDays(new Date(`${weekStart.value}T00:00:00Z`), -7)))
  const thisWeek = () => goTo(isoDate(mondayOf(new Date())))

  function select(shiftId: string | null) {
    selectedShiftId.value = shiftId
  }

  /**
   * Assigns and reloads. Returns the ApiError on a rule conflict so the caller can show the
   * violations (and offer the hours override), and rethrows anything else.
   */
  async function assign(shiftId: string, technicianId: string, overrideHours = false): Promise<ApiError | null> {
    try {
      await shiftsApi.assign(shiftId, technicianId, overrideHours)
      await load()
      return null
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) return error
      throw error
    }
  }

  async function unassign(shiftId: string, assignmentId: string) {
    await shiftsApi.unassign(shiftId, assignmentId)
    await load()
  }

  async function createShift(request: ShiftRequest) {
    const shift = await shiftsApi.create(request)
    await load()
    return shift
  }

  async function updateShift(id: string, request: ShiftRequest) {
    const shift = await shiftsApi.update(id, request)
    await load()
    return shift
  }

  async function deleteShift(id: string) {
    await shiftsApi.remove(id)
    if (selectedShiftId.value === id) selectedShiftId.value = null
    await load()
  }

  async function autoAssign(dryRun: boolean): Promise<AutoAssignResult> {
    const result = await scheduleApi.autoAssign(weekStart.value, dryRun)
    if (!dryRun) await load()
    return result
  }

  async function clearAuto(): Promise<number> {
    const { removed } = await scheduleApi.clearAuto(weekStart.value)
    await load()
    return removed
  }

  const exportUrl = computed(() => scheduleApi.exportUrl(weekStart.value))

  return {
    weekStart,
    week,
    loading,
    siteFilter,
    selectedShiftId,
    selectedShift,
    draggingTechnicianId,
    days,
    visibleShifts,
    shiftsByDay,
    openSlots,
    exportUrl,
    load,
    goTo,
    nextWeek,
    previousWeek,
    thisWeek,
    select,
    assign,
    unassign,
    createShift,
    updateShift,
    deleteShift,
    autoAssign,
    clearAuto,
  }
})
