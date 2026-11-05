import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { scheduleApi, shiftsApi } from '@/api'
import { ApiError } from '@/api/client'
import type { AutoAssignResult, Shift, ShiftRequest, WeekSchedule } from '@/api/types'
import { addDays, dateOf, mondayOf, weekDays } from '@/lib/week'
import { useConfigStore } from '@/stores/config'

/** The week on screen and every action that changes it. Each mutation reloads the week from the API. */
export const useScheduleStore = defineStore('schedule', () => {
  const config = useConfigStore()
  /** The Monday of the current week, as the installation's time zone sees it. */
  const currentMonday = () => mondayOf(new Date(), config.timeZone)

  const weekStart = ref(currentMonday())
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
      map.get(dateOf(shift.startsAt, config.timeZone))?.push(shift)
    }
    return map
  })

  const selectedShift = computed(() => week.value?.shifts.find((s) => s.id === selectedShiftId.value) ?? null)

  const openSlots = computed(() => visibleShifts.value.reduce((n, s) => n + Math.max(0, s.headcount - s.assignments.length), 0))

  /** Bumped on every load so a response that arrives after a newer request started is ignored. */
  let loadSeq = 0

  async function load() {
    const seq = ++loadSeq
    loading.value = true
    try {
      const result = await scheduleApi.week(weekStart.value)
      if (seq === loadSeq) week.value = result
    } finally {
      if (seq === loadSeq) loading.value = false
    }
  }

  /** Forgets the loaded week and the selection, e.g. when the user signs out. In-flight loads are dropped. */
  function reset() {
    loadSeq++
    loading.value = false
    week.value = null
    siteFilter.value = null
    selectedShiftId.value = null
    draggingTechnicianId.value = null
    weekStart.value = currentMonday()
  }

  function goTo(start: string) {
    weekStart.value = start
    return load()
  }

  const nextWeek = () => goTo(addDays(weekStart.value, 7))
  const previousWeek = () => goTo(addDays(weekStart.value, -7))
  const thisWeek = () => goTo(currentMonday())

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
    reset,
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
