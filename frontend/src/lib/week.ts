/**
 * Date helpers for the Monday-to-Sunday week the planner works in. All pure.
 *
 * Instants (shift startsAt/endsAt) are ISO-8601 UTC strings; the user sees and types them as
 * wall-clock times in the installation's IANA time zone, which every helper takes explicitly.
 * Weeks and days are plain `YYYY-MM-DD` dates, the same in every zone.
 */

export const DAY_NAMES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const

const MINUTE = 60_000
const DAY = 24 * 60 * MINUTE

interface WallClock {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

const formatters = new Map<string, Intl.DateTimeFormat>()

function formatterFor(zone: string): Intl.DateTimeFormat {
  let fmt = formatters.get(zone)
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
    formatters.set(zone, fmt)
  }
  return fmt
}

/** True when `zone` is an IANA id this runtime knows (`Intl` throws on an unknown one). */
export function isValidTimeZone(zone: string): boolean {
  try {
    formatterFor(zone)
    return true
  } catch {
    return false
  }
}

/** The wall clock the instant shows in `zone`. */
function wallClockOf(date: Date, zone: string): WallClock {
  const parts: Partial<WallClock> = {}
  for (const part of formatterFor(zone).formatToParts(date)) {
    if (part.type !== 'literal') parts[part.type as keyof WallClock] = Number(part.value)
  }
  return {
    year: parts.year ?? 1970,
    month: parts.month ?? 1,
    day: parts.day ?? 1,
    // Some engines print midnight as 24 even with hourCycle h23.
    hour: (parts.hour ?? 0) % 24,
    minute: parts.minute ?? 0,
    second: parts.second ?? 0,
  }
}

/** The zone's UTC offset in milliseconds at the given instant (positive east of Greenwich). */
function offsetAt(ms: number, zone: string): number {
  const whole = Math.floor(ms / 1000) * 1000
  const w = wallClockOf(new Date(whole), zone)
  return Date.UTC(w.year, w.month - 1, w.day, w.hour, w.minute, w.second) - whole
}

const pad = (n: number) => String(n).padStart(2, '0')

function parseDay(day: string): [number, number, number] {
  const [y, m, d] = day.split('-').map(Number)
  if (y === undefined || m === undefined || d === undefined || Number.isNaN(y + m + d)) {
    throw new RangeError(`Invalid day: ${day}`)
  }
  return [y, m, d]
}

/**
 * The UTC ISO instant of a wall-clock `day` + `time` (`HH:MM`) in `zone`.
 *
 * The zone's offset is read on either side of the moment (a day before and after, so both offsets
 * around a DST change are seen) and each candidate instant is checked against its own offset.
 * A time that happens twice (clocks going back) resolves to its first occurrence; a time that does
 * not exist (clocks going forward) moves forward by the length of the gap, like most libraries do.
 */
export function zonedToUtc(day: string, time: string, zone: string): string {
  const [y, m, d] = parseDay(day)
  const [hh, mm] = time.split(':').map(Number)
  const wall = Date.UTC(y, m - 1, d, hh ?? 0, mm ?? 0)
  const candidates = [...new Set([offsetAt(wall - DAY, zone), offsetAt(wall + DAY, zone)])].map((offset) => ({
    offset,
    instant: wall - offset,
  }))
  const valid = candidates.filter((c) => offsetAt(c.instant, zone) === c.offset).map((c) => c.instant)
  const instant = valid.length > 0 ? Math.min(...valid) : Math.max(...candidates.map((c) => c.instant))
  return new Date(instant).toISOString()
}

/** `YYYY-MM-DD` of the instant, in `zone`. */
export function isoDate(date: Date, zone: string): string {
  const w = wallClockOf(date, zone)
  return `${w.year}-${pad(w.month)}-${pad(w.day)}`
}

/** `YYYY-MM-DD` of an ISO instant, in `zone`. */
export function dateOf(iso: string, zone: string): string {
  return isoDate(new Date(iso), zone)
}

/** `HH:MM` of an ISO instant, in `zone`. */
export function timeOf(iso: string, zone: string): string {
  const w = wallClockOf(new Date(iso), zone)
  return `${pad(w.hour)}:${pad(w.minute)}`
}

/** Plain-date arithmetic: the `YYYY-MM-DD` that is `days` after `day`. */
export function addDays(day: string, days: number): string {
  const [y, m, d] = parseDay(day)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}

/** The Monday (`YYYY-MM-DD`) that starts the ISO week containing the plain date `day`. */
export function mondayOfDay(day: string): string {
  const [y, m, d] = parseDay(day)
  const offset = (new Date(Date.UTC(y, m - 1, d)).getUTCDay() + 6) % 7
  return addDays(day, -offset)
}

/** The Monday (`YYYY-MM-DD`) that starts the week containing the instant, as seen in `zone`. */
export function mondayOf(date: Date, zone: string): string {
  return mondayOfDay(isoDate(date, zone))
}

/** The seven `YYYY-MM-DD` dates of the week that starts on `weekStart`. */
export function weekDays(weekStart: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(weekStart, i))
}

export function formatDay(isoDay: string): string {
  const d = new Date(`${isoDay}T00:00:00Z`)
  return `${DAY_NAMES[(d.getUTCDay() + 6) % 7]} ${d.getUTCDate()}`
}

export function formatRange(weekStart: string, weekEnd: string): string {
  const fmt = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' })
  const start = new Date(`${weekStart}T00:00:00Z`)
  const end = new Date(`${weekEnd}T00:00:00Z`)
  return `${fmt.format(start)} – ${fmt.format(end)} ${end.getUTCFullYear()}`
}

export function hoursLabel(hours: number): string {
  return Number.isInteger(hours) ? `${hours} h` : `${hours.toFixed(1)} h`
}

/** Real elapsed hours between two ISO instants, DST changes included. */
export function hoursBetween(startIso: string, endIso: string): number {
  return (Date.parse(endIso) - Date.parse(startIso)) / (60 * MINUTE)
}
