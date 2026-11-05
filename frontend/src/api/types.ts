// Mirrors backend/src/JuDesk.Api/Contracts/Dtos.cs. Instants are ISO-8601 UTC strings; week dates are plain YYYY-MM-DD
// interpreted in the installation's time zone (see AppConfig).

export type UserRole = 'Planner' | 'Admin'
export type AssignmentSource = 'Manual' | 'Auto'
export type ViolationCode =
  'TechnicianInactive' | 'MissingSkill' | 'AlreadyAssigned' | 'ShiftFull' | 'Overlap' | 'MaxHoursExceeded'

export interface SetupStatus {
  needsSetup: boolean
}

/** Public installation settings (GET /api/config). `timeZone` is an IANA id; every wall-clock time the user sees is in it. */
export interface AppConfig {
  timeZone: string
  passwordMinLength: number
}

export interface DemoStatus {
  enabled: boolean
  resetMinutes: number
  nextResetAt: string | null
}

export interface SetupRequest {
  code: string
  email: string
  displayName: string
  password: string
}

export interface User {
  id: string
  email: string
  displayName: string
  role: UserRole
  /** True only for the shared demo account, which cannot change its password. */
  isDemo: boolean
}

export interface Skill {
  id: string
  name: string
}

export interface Technician {
  id: string
  fullName: string
  email: string | null
  phone: string | null
  maxWeeklyHours: number
  active: boolean
  skills: Skill[]
}

export interface TechnicianRequest {
  fullName: string
  email: string | null
  phone: string | null
  maxWeeklyHours: number
  active: boolean
  skillIds: string[]
}

export interface Site {
  id: string
  name: string
  address: string | null
  color: string | null
}

export interface SiteRequest {
  name: string
  address: string | null
  color: string | null
}

export interface Assignment {
  id: string
  technicianId: string
  technicianName: string
  source: AssignmentSource
}

export interface Shift {
  id: string
  siteId: string
  siteName: string
  siteColor: string | null
  startsAt: string
  endsAt: string
  hours: number
  requiredSkillId: string
  requiredSkillName: string
  headcount: number
  notes: string | null
  assignments: Assignment[]
}

export interface ShiftRequest {
  siteId: string
  startsAt: string
  endsAt: string
  requiredSkillId: string
  headcount: number
  notes: string | null
}

export interface Violation {
  code: ViolationCode
  message: string
  conflictingShiftId: string | null
}

export interface Candidate {
  technicianId: string
  fullName: string
  scheduledHours: number
  maxWeeklyHours: number
  eligible: boolean
  violations: Violation[]
}

export interface TechnicianLoad {
  id: string
  fullName: string
  active: boolean
  maxWeeklyHours: number
  scheduledHours: number
  skills: string[]
}

export interface WeekSchedule {
  weekStart: string
  weekEnd: string
  sites: Site[]
  shifts: Shift[]
  technicians: TechnicianLoad[]
  openSlots: number
}

export interface AutoAssignResult {
  created: number
  assignments: { shiftId: string; technicianId: string; technicianName: string }[]
  unfilled: { shiftId: string; missing: number; reason: string }[]
  dryRun: boolean
}

export interface CreateUserRequest {
  email: string
  displayName: string
  password: string
  role: UserRole
}
