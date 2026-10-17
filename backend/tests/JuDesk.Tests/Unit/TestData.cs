using JuDesk.Domain.Entities;

namespace JuDesk.Tests.Unit;

/// <summary>In-memory builders for the pure domain tests. Monday 2026-10-12 is the reference week.</summary>
internal static class TestData
{
    public static readonly DateTime Monday = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
    public static readonly Skill Electrical = new() { Name = "Electrical" };
    public static readonly Skill Hvac = new() { Name = "HVAC" };
    public static readonly Site Hospital = new() { Name = "Hospital" };

    public static Technician Tech(string name, int maxHours = 40, bool active = true, params Skill[] skills) =>
        new() { FullName = name, MaxWeeklyHours = maxHours, Active = active, Skills = skills.ToList() };

    public static Shift ShiftAt(int day, int hour, int hours, Skill skill, int headcount = 1, Site? site = null)
    {
        var start = Monday.AddDays(day).AddHours(hour);
        var s = site ?? Hospital;
        return new Shift
        {
            Site = s, SiteId = s.Id,
            StartsAt = start, EndsAt = start.AddHours(hours),
            RequiredSkill = skill, RequiredSkillId = skill.Id,
            Headcount = headcount,
        };
    }

    public static Shift Assigned(this Shift shift, params Technician[] technicians)
    {
        foreach (var t in technicians)
        {
            var a = new Assignment { Shift = shift, ShiftId = shift.Id, Technician = t, TechnicianId = t.Id };
            shift.Assignments.Add(a);
            t.Assignments.Add(a);
        }
        return shift;
    }
}
