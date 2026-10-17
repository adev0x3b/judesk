using JuDesk.Domain.Entities;

namespace JuDesk.Domain.Scheduling;

/// <summary>
/// The scheduling constraints, as pure functions over in-memory entities so they can be unit tested
/// and reused by both the manual assign endpoint and the auto-scheduler.
/// </summary>
public static class AssignmentRules
{
    /// <summary>
    /// Checks whether <paramref name="technician"/> may be added to <paramref name="shift"/>.
    /// </summary>
    /// <param name="technician">The candidate, with Skills loaded.</param>
    /// <param name="shift">The target shift, with RequiredSkill and Assignments loaded.</param>
    /// <param name="technicianShiftsInWeek">
    /// Every shift the technician is already assigned to in the ISO week that contains the target
    /// shift, excluding the target shift itself.
    /// </param>
    public static IReadOnlyList<Violation> Check(Technician technician, Shift shift, IEnumerable<Shift> technicianShiftsInWeek)
    {
        var violations = new List<Violation>();

        if (!technician.Active)
        {
            violations.Add(new Violation(ViolationCode.TechnicianInactive, $"{technician.FullName} is inactive."));
        }

        if (technician.Skills.All(s => s.Id != shift.RequiredSkillId))
        {
            var skillName = shift.RequiredSkill?.Name ?? "the required skill";
            violations.Add(new Violation(ViolationCode.MissingSkill, $"{technician.FullName} does not have {skillName}."));
        }

        if (shift.Assignments.Any(a => a.TechnicianId == technician.Id))
        {
            violations.Add(new Violation(ViolationCode.AlreadyAssigned, $"{technician.FullName} is already on this shift."));
        }
        else if (shift.Assignments.Count >= shift.Headcount)
        {
            violations.Add(new Violation(ViolationCode.ShiftFull, $"This shift already has {shift.Headcount} technician(s)."));
        }

        var existing = technicianShiftsInWeek.Where(s => s.Id != shift.Id).ToList();

        foreach (var other in existing.Where(shift.Overlaps).OrderBy(s => s.StartsAt))
        {
            violations.Add(new Violation(
                ViolationCode.Overlap,
                $"Overlaps with {other.Site?.Name ?? "another shift"} {other.StartsAt:ddd HH:mm}-{other.EndsAt:HH:mm}.",
                other.Id));
        }

        var scheduled = existing.Sum(s => s.Hours);
        var projected = scheduled + shift.Hours;
        if (projected > technician.MaxWeeklyHours)
        {
            violations.Add(new Violation(
                ViolationCode.MaxHoursExceeded,
                $"Would reach {projected:0.#} h this week, over the {technician.MaxWeeklyHours} h limit."));
        }

        return violations;
    }
}
