import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { WeekSchedule } from '@/api/types'
import { useScheduleStore } from '@/stores/schedule'

const week: WeekSchedule = {
  weekStart: '2026-10-12',
  weekEnd: '2026-10-18',
  sites: [],
  technicians: [],
  openSlots: 1,
  shifts: [
    {
      id: 's1',
      siteId: 'a',
      siteName: 'A',
      siteColor: null,
      startsAt: '2026-10-12T06:00:00Z',
      endsAt: '2026-10-12T14:00:00Z',
      hours: 8,
      requiredSkillId: 'k',
      requiredSkillName: 'K',
      headcount: 1,
      notes: null,
      assignments: [],
    },
    {
      id: 's2',
      siteId: 'b',
      siteName: 'B',
      siteColor: null,
      startsAt: '2026-10-14T06:00:00Z',
      endsAt: '2026-10-14T14:00:00Z',
      hours: 8,
      requiredSkillId: 'k',
      requiredSkillName: 'K',
      headcount: 1,
      notes: null,
      assignments: [{ id: 'x', technicianId: 't', technicianName: 'T', source: 'Manual' }],
    },
  ],
}

vi.mock('@/api', () => ({
  scheduleApi: {
    week: vi.fn<() => Promise<WeekSchedule>>(() => Promise.resolve(week)),
    autoAssign: vi.fn<() => Promise<unknown>>(),
    clearAuto: vi.fn<() => Promise<unknown>>(),
    exportUrl: (w: string) => `/api/schedule/export?weekStart=${w}`,
  },
  shiftsApi: {
    assign: vi.fn<() => Promise<unknown>>(() => Promise.resolve({})),
    unassign: vi.fn<() => Promise<void>>(),
    create: vi.fn<() => Promise<unknown>>(),
    update: vi.fn<() => Promise<unknown>>(),
    remove: vi.fn<() => Promise<void>>(),
  },
}))

describe('schedule store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('groups the loaded shifts by day and filters by site', async () => {
    const store = useScheduleStore()
    store.weekStart = '2026-10-12'

    await store.load()

    expect(store.days).toHaveLength(7)
    expect(store.shiftsByDay.get('2026-10-12')?.map((s) => s.id)).toEqual(['s1'])
    expect(store.shiftsByDay.get('2026-10-14')?.map((s) => s.id)).toEqual(['s2'])
    expect(store.openSlots).toBe(1)

    store.siteFilter = 'b'
    expect(store.visibleShifts.map((s) => s.id)).toEqual(['s2'])
    expect(store.openSlots).toBe(0)
  })

  it('moves between weeks', async () => {
    const { scheduleApi } = await import('@/api')
    const store = useScheduleStore()
    store.weekStart = '2026-10-12'

    await store.nextWeek()
    expect(store.weekStart).toBe('2026-10-19')
    expect(scheduleApi.week).toHaveBeenLastCalledWith('2026-10-19')
    await store.previousWeek()
    await store.previousWeek()
    expect(store.weekStart).toBe('2026-10-05')
    expect(scheduleApi.week).toHaveBeenLastCalledWith('2026-10-05')
    expect(store.exportUrl).toBe('/api/schedule/export?weekStart=2026-10-05')
  })

  it('ignores a week response that arrives after a newer request started', async () => {
    const { scheduleApi } = await import('@/api')
    const store = useScheduleStore()
    store.weekStart = '2026-10-12'
    let resolveFirst: (w: WeekSchedule) => void = () => {}
    vi.mocked(scheduleApi.week).mockImplementationOnce(() => new Promise((resolve) => (resolveFirst = resolve)))
    const later: WeekSchedule = { ...week, weekStart: '2026-10-19', weekEnd: '2026-10-25', shifts: [] }
    vi.mocked(scheduleApi.week).mockResolvedValueOnce(later)

    const first = store.load()
    await store.nextWeek()
    expect(store.week?.weekStart).toBe('2026-10-19')
    expect(store.loading).toBe(false)

    resolveFirst(week)
    await first
    expect(store.week?.weekStart).toBe('2026-10-19')
    expect(store.loading).toBe(false)
  })

  it('forgets the week and the selection on reset', async () => {
    const store = useScheduleStore()
    store.weekStart = '2026-10-12'
    await store.load()
    store.select('s1')
    store.siteFilter = 'a'

    store.reset()

    expect(store.week).toBeNull()
    expect(store.selectedShift).toBeNull()
    expect(store.siteFilter).toBeNull()
  })

  it('reloads after an assignment and reports rule conflicts instead of throwing', async () => {
    const { shiftsApi, scheduleApi } = await import('@/api')
    const { ApiError } = await import('@/api/client')
    const store = useScheduleStore()
    store.weekStart = '2026-10-12'

    expect(await store.assign('s1', 't')).toBeNull()
    expect(scheduleApi.week).toHaveBeenCalled()

    vi.mocked(shiftsApi.assign).mockRejectedValueOnce(
      new ApiError(409, { title: 'No', violations: [{ code: 'Overlap', message: 'm', conflictingShiftId: null }] }),
    )
    const rejected = await store.assign('s1', 't')
    expect(rejected?.violations[0]?.code).toBe('Overlap')

    vi.mocked(shiftsApi.assign).mockRejectedValueOnce(new ApiError(500, null))
    await expect(store.assign('s1', 't')).rejects.toBeInstanceOf(ApiError)
  })
})
