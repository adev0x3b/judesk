import { describe, expect, it } from 'vitest'
import {
  addDays,
  dateOf,
  formatDay,
  formatRange,
  hoursBetween,
  hoursLabel,
  isoDate,
  isValidTimeZone,
  mondayOf,
  mondayOfDay,
  timeOf,
  weekDays,
  zonedToUtc,
} from '@/lib/week'

const ROME = 'Europe/Rome'

describe('week helpers', () => {
  it('snaps any day to its Monday', () => {
    expect(mondayOf(new Date('2026-10-14T10:00:00Z'), 'UTC')).toBe('2026-10-12')
    expect(mondayOf(new Date('2026-10-12T00:00:00Z'), 'UTC')).toBe('2026-10-12')
    expect(mondayOf(new Date('2026-10-18T23:59:59Z'), 'UTC')).toBe('2026-10-12')
    expect(mondayOfDay('2026-10-18')).toBe('2026-10-12')
    expect(mondayOfDay('2026-10-12')).toBe('2026-10-12')
  })

  it('finds the Monday in the installation zone, not in UTC', () => {
    // Sunday 18 Oct 23:30 in Rome is still Sunday, but 21:30Z the same day; Sunday 22:30Z is already Monday 00:30 in Rome.
    expect(mondayOf(new Date('2026-10-18T22:30:00Z'), ROME)).toBe('2026-10-19')
    expect(mondayOf(new Date('2026-10-18T22:30:00Z'), 'UTC')).toBe('2026-10-12')
    expect(isoDate(new Date('2026-10-18T22:30:00Z'), ROME)).toBe('2026-10-19')
    expect(isoDate(new Date('2026-10-18T22:30:00Z'), 'UTC')).toBe('2026-10-18')
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
    expect(addDays('2026-12-28', 7)).toBe('2027-01-04')
    expect(addDays('2027-01-04', -7)).toBe('2026-12-28')
  })

  it('formats days and ranges from plain dates', () => {
    expect(formatDay('2026-10-12')).toBe('Mon 12')
    expect(formatRange('2026-10-12', '2026-10-18')).toBe('12 Oct – 18 Oct 2026')
  })

  it('shows and reads times in UTC when that is the zone', () => {
    expect(timeOf('2026-10-12T06:30:00Z', 'UTC')).toBe('06:30')
    expect(dateOf('2026-10-12T06:30:00Z', 'UTC')).toBe('2026-10-12')
    expect(zonedToUtc('2026-10-12', '08:00', 'UTC')).toBe('2026-10-12T08:00:00.000Z')
    expect(zonedToUtc('2026-10-12', '00:00', 'UTC')).toBe('2026-10-12T00:00:00.000Z')
  })

  it('shows and reads times in the installation zone', () => {
    // Winter: CET, UTC+1.
    expect(timeOf('2026-11-02T07:00:00Z', ROME)).toBe('08:00')
    expect(dateOf('2026-11-02T23:30:00Z', ROME)).toBe('2026-11-03')
    expect(zonedToUtc('2026-11-02', '08:00', ROME)).toBe('2026-11-02T07:00:00.000Z')
    expect(zonedToUtc('2026-11-03', '00:30', ROME)).toBe('2026-11-02T23:30:00.000Z')
    // Summer: CEST, UTC+2.
    expect(timeOf('2026-07-01T06:00:00Z', ROME)).toBe('08:00')
    expect(zonedToUtc('2026-07-01', '08:00', ROME)).toBe('2026-07-01T06:00:00.000Z')
    // What the form reads back is what it sent.
    const iso = zonedToUtc('2026-07-01', '22:15', ROME)
    expect(dateOf(iso, ROME)).toBe('2026-07-01')
    expect(timeOf(iso, ROME)).toBe('22:15')
  })

  it('handles a zone with a half-hour offset', () => {
    expect(zonedToUtc('2026-10-12', '08:00', 'Asia/Kolkata')).toBe('2026-10-12T02:30:00.000Z')
    expect(timeOf('2026-10-12T02:30:00Z', 'Asia/Kolkata')).toBe('08:00')
  })

  it('moves a time that does not exist forward past the spring DST gap', () => {
    // Clocks jump from 02:00 to 03:00 CEST at 01:00Z on 29 Mar 2026: 02:00 never happens.
    expect(zonedToUtc('2026-03-29', '01:59', ROME)).toBe('2026-03-29T00:59:00.000Z')
    expect(zonedToUtc('2026-03-29', '02:00', ROME)).toBe('2026-03-29T01:00:00.000Z')
    expect(zonedToUtc('2026-03-29', '02:30', ROME)).toBe('2026-03-29T01:30:00.000Z')
    expect(zonedToUtc('2026-03-29', '03:00', ROME)).toBe('2026-03-29T01:00:00.000Z')
    expect(timeOf('2026-03-29T01:00:00Z', ROME)).toBe('03:00')
  })

  it('picks the first occurrence of a time that happens twice in the autumn DST overlap', () => {
    // Clocks go back from 03:00 CEST to 02:00 CET at 01:00Z on 25 Oct 2026: 02:30 happens at 00:30Z and 01:30Z.
    expect(zonedToUtc('2026-10-25', '02:30', ROME)).toBe('2026-10-25T00:30:00.000Z')
    expect(zonedToUtc('2026-10-25', '01:59', ROME)).toBe('2026-10-24T23:59:00.000Z')
    expect(zonedToUtc('2026-10-25', '03:00', ROME)).toBe('2026-10-25T02:00:00.000Z')
    expect(timeOf('2026-10-25T00:30:00Z', ROME)).toBe('02:30')
    expect(timeOf('2026-10-25T01:30:00Z', ROME)).toBe('02:30')
  })

  it('counts real hours across the autumn change', () => {
    const start = zonedToUtc('2026-10-24', '22:00', ROME)
    const end = zonedToUtc('2026-10-25', '06:00', ROME)
    expect(start).toBe('2026-10-24T20:00:00.000Z')
    expect(end).toBe('2026-10-25T05:00:00.000Z')
    expect(hoursBetween(start, end)).toBe(9)
    expect(hoursBetween(zonedToUtc('2026-03-28', '22:00', ROME), zonedToUtc('2026-03-29', '06:00', ROME))).toBe(7)
    expect(hoursBetween(zonedToUtc('2026-10-24', '22:00', 'UTC'), zonedToUtc('2026-10-25', '06:00', 'UTC'))).toBe(8)
  })

  it('knows which zone ids the runtime accepts', () => {
    expect(isValidTimeZone('Europe/Rome')).toBe(true)
    expect(isValidTimeZone('UTC')).toBe(true)
    expect(isValidTimeZone('Mars/Olympus')).toBe(false)
    expect(isValidTimeZone('')).toBe(false)
  })

  it('labels hours compactly', () => {
    expect(hoursLabel(8)).toBe('8 h')
    expect(hoursLabel(7.5)).toBe('7.5 h')
  })
})
