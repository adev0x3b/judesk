using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;

namespace JuDesk.Api.Contracts;

public static class Mapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.DisplayName, u.Role);

    public static SkillDto ToDto(this Skill s) => new(s.Id, s.Name);

    public static TechnicianDto ToDto(this Technician t) => new(
        t.Id, t.FullName, t.Email, t.Phone, t.MaxWeeklyHours, t.Active,
        t.Skills.OrderBy(s => s.Name).Select(ToDto).ToList());

    public static SiteDto ToDto(this Site s) => new(s.Id, s.Name, s.Address, s.Color);

    public static AssignmentDto ToDto(this Assignment a) => new(a.Id, a.TechnicianId, a.Technician?.FullName ?? string.Empty, a.Source);

    public static ShiftDto ToDto(this Shift s) => new(
        s.Id, s.SiteId, s.Site?.Name ?? string.Empty, s.Site?.Color,
        s.StartsAt, s.EndsAt, Math.Round(s.Hours, 2),
        s.RequiredSkillId, s.RequiredSkill?.Name ?? string.Empty,
        s.Headcount, s.Notes,
        s.Assignments.OrderBy(a => a.CreatedAt).Select(ToDto).ToList());

    public static ViolationDto ToDto(this Violation v) => new(v.Code, v.Message, v.ConflictingShiftId);
}
