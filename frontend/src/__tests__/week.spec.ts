import { describe, expect, it } from 'vitest'
import { formatDay, formatRange, hoursLabel, isoDate, mondayOf, timeOf, toIso, weekDays } from '@/lib/week'

describe('week helpers', () => {
  it('snaps any day to its Monday', () => {
    expect(isoDate(mondayOf(new Date('2026-10-14T10:00:00Z')))).toBe('2026-10-12')
    expect(isoDate(mondayOf(new Date('2026-10-12T00:00:00Z')))).toBe('2026-10-12')
    expect(isoDate(mondayOf(new Date('2026-10-18T23:59:59Z')))).toBe('2026-10-12')
  })

  it('lists the seven days of a week', () => {
    expect(weekDays('2026-10-12')).toEqual([
      '2026-10-12',
      '2026-10-13',
      '2026-10-14',
      '2026-10-15',
      '2026-10-16',
      '2026-10-17',
      '2026-10-18',
    ])
  })

  it('crosses month and year boundaries', () => {
    expect(weekDays('2026-12-28')[6]).toBe('2027-01-03')
  })

  it('formats times, days and ranges in UTC', () => {
    expect(timeOf('2026-10-12T06:30:00Z')).toBe('06:30')
    expect(formatDay('2026-10-12')).toBe('Mon 12')
    expect(formatRange('2026-10-12', '2026-10-18')).toBe('12 Oct – 18 Oct 2026')
    expect(toIso('2026-10-12', '08:00')).toBe('2026-10-12T08:00:00Z')
  })

  it('labels hours compactly', () => {
    expect(hoursLabel(8)).toBe('8 h')
    expect(hoursLabel(7.5)).toBe('7.5 h')
  })
})
