/** Date helpers for the Monday-to-Sunday week the planner works in. All pure, all UTC. */

export const DAY_NAMES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const

/** `YYYY-MM-DD` of the given instant, in UTC. */
export function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10)
}

/** The Monday that starts the ISO week containing `date` (UTC). */
export function mondayOf(date: Date): Date {
  const utc = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()))
  const offset = (utc.getUTCDay() + 6) % 7
  utc.setUTCDate(utc.getUTCDate() - offset)
  return utc
}

export function addDays(date: Date, days: number): Date {
  const copy = new Date(date.getTime())
  copy.setUTCDate(copy.getUTCDate() + days)
  return copy
}

/** The seven `YYYY-MM-DD` dates of the week that starts on `weekStart`. */
export function weekDays(weekStart: string): string[] {
  const monday = new Date(`${weekStart}T00:00:00Z`)
  return Array.from({ length: 7 }, (_, i) => isoDate(addDays(monday, i)))
}

/** `HH:MM` of an ISO instant, in UTC. The whole app shows times in UTC, like the API. */
export function timeOf(iso: string): string {
  return iso.slice(11, 16)
}

export function dateOf(iso: string): string {
  return iso.slice(0, 10)
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

/** Builds the UTC ISO instant for a form's day + time fields. */
export function toIso(day: string, time: string): string {
  return `${day}T${time}:00Z`
}
