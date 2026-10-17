using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;

namespace JuDesk.Api.Contracts;

// Auth
public sealed record LoginRequest(string Email, string Password);
public sealed record UserDto(Guid Id, string Email, string DisplayName, UserRole Role);
public sealed record CreateUserRequest(string Email, string DisplayName, string Password, UserRole Role);

// Skills
public sealed record SkillDto(Guid Id, string Name);
public sealed record SkillRequest(string Name);

// Technicians
public sealed record TechnicianDto(Guid Id, string FullName, string? Email, string? Phone, int MaxWeeklyHours, bool Active, IReadOnlyList<SkillDto> Skills);
public sealed record TechnicianRequest(string FullName, string? Email, string? Phone, int MaxWeeklyHours, bool Active, IReadOnlyList<Guid> SkillIds);

// Sites
public sealed record SiteDto(Guid Id, string Name, string? Address, string? Color);
public sealed record SiteRequest(string Name, string? Address, string? Color);

// Shifts
public sealed record AssignmentDto(Guid Id, Guid TechnicianId, string TechnicianName, AssignmentSource Source);
public sealed record ShiftDto(
    Guid Id,
    Guid SiteId,
    string SiteName,
    string? SiteColor,
    DateTime StartsAt,
    DateTime EndsAt,
    double Hours,
    Guid RequiredSkillId,
    string RequiredSkillName,
    int Headcount,
    string? Notes,
    IReadOnlyList<AssignmentDto> Assignments);
public sealed record ShiftRequest(Guid SiteId, DateTime StartsAt, DateTime EndsAt, Guid RequiredSkillId, int Headcount, string? Notes);

// Assignments
public sealed record AssignRequest(Guid TechnicianId, bool OverrideHours = false);
public sealed record ViolationDto(ViolationCode Code, string Message, Guid? ConflictingShiftId);
public sealed record CandidateDto(Guid TechnicianId, string FullName, double ScheduledHours, int MaxWeeklyHours, bool Eligible, IReadOnlyList<ViolationDto> Violations);

// Schedule
public sealed record TechnicianLoadDto(Guid Id, string FullName, bool Active, int MaxWeeklyHours, double ScheduledHours, IReadOnlyList<string> Skills);
public sealed record WeekScheduleDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<SiteDto> Sites,
    IReadOnlyList<ShiftDto> Shifts,
    IReadOnlyList<TechnicianLoadDto> Technicians,
    int OpenSlots);
public sealed record AutoAssignRequest(DateOnly WeekStart, bool DryRun = false);
public sealed record UnfilledSlotDto(Guid ShiftId, int Missing, string Reason);
public sealed record AutoAssignResultDto(int Created, IReadOnlyList<AssignmentPlanDto> Assignments, IReadOnlyList<UnfilledSlotDto> Unfilled, bool DryRun);
public sealed record AssignmentPlanDto(Guid ShiftId, Guid TechnicianId, string TechnicianName);
