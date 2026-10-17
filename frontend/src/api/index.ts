import { http } from './client'
import type {
  Assignment,
  AutoAssignResult,
  Candidate,
  CreateUserRequest,
  Shift,
  ShiftRequest,
  Site,
  SiteRequest,
  Skill,
  Technician,
  TechnicianRequest,
  User,
  WeekSchedule,
} from './types'

export const authApi = {
  login: (email: string, password: string) => http.post<User>('/api/auth/login', { email, password }),
  logout: () => http.post<void>('/api/auth/logout'),
  me: () => http.get<User>('/api/auth/me'),
}

export const usersApi = {
  list: () => http.get<User[]>('/api/users'),
  create: (request: CreateUserRequest) => http.post<User>('/api/users', request),
  remove: (id: string) => http.delete(`/api/users/${id}`),
}

export const skillsApi = {
  list: () => http.get<Skill[]>('/api/skills'),
  create: (name: string) => http.post<Skill>('/api/skills', { name }),
  remove: (id: string) => http.delete(`/api/skills/${id}`),
}

export const techniciansApi = {
  list: () => http.get<Technician[]>('/api/technicians'),
  create: (request: TechnicianRequest) => http.post<Technician>('/api/technicians', request),
  update: (id: string, request: TechnicianRequest) => http.put<Technician>(`/api/technicians/${id}`, request),
  remove: (id: string) => http.delete(`/api/technicians/${id}`),
}

export const sitesApi = {
  list: () => http.get<Site[]>('/api/sites'),
  create: (request: SiteRequest) => http.post<Site>('/api/sites', request),
  update: (id: string, request: SiteRequest) => http.put<Site>(`/api/sites/${id}`, request),
  remove: (id: string) => http.delete(`/api/sites/${id}`),
}

export const shiftsApi = {
  create: (request: ShiftRequest) => http.post<Shift>('/api/shifts', request),
  update: (id: string, request: ShiftRequest) => http.put<Shift>(`/api/shifts/${id}`, request),
  remove: (id: string) => http.delete(`/api/shifts/${id}`),
  candidates: (id: string) => http.get<Candidate[]>(`/api/shifts/${id}/candidates`),
  assign: (id: string, technicianId: string, overrideHours = false) =>
    http.post<Assignment>(`/api/shifts/${id}/assignments`, { technicianId, overrideHours }),
  unassign: (id: string, assignmentId: string) => http.delete(`/api/shifts/${id}/assignments/${assignmentId}`),
}

export const scheduleApi = {
  week: (weekStart: string) => http.get<WeekSchedule>(`/api/schedule/week?weekStart=${weekStart}`),
  autoAssign: (weekStart: string, dryRun: boolean) =>
    http.post<AutoAssignResult>('/api/schedule/auto-assign', { weekStart, dryRun }),
  clearAuto: (weekStart: string) => http.delete<{ removed: number }>(`/api/schedule/auto-assignments?weekStart=${weekStart}`),
  exportUrl: (weekStart: string) => `/api/schedule/export?weekStart=${weekStart}`,
}
