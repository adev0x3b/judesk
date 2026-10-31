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
    /// <param name="technicianShifts">
    /// Every shift the technician is already assigned to in the ISO week that contains the target
    /// shift, plus any on the days around it: all of them count for the overlap check, only the
    /// ones starting in that week count towards the weekly hours. The target shift itself is ignored.
    /// </param>
    public static IReadOnlyList<Violation> Check(Technician technician, Shift shift, IEnumerable<Shift> technicianShifts) =>
        Check(technician, shift, technicianShifts, shift.Assignments.Count, shift.Assignments.Any(a => a.TechnicianId == technician.Id));

    /// <summary>
    /// Same check with the shift's occupancy given explicitly, for callers that plan several
    /// assignments in memory before any of them is stored (the auto-scheduler).
    /// </summary>
    /// <param name="assignedCount">How many technicians are on the shift right now.</param>
    /// <param name="alreadyAssigned">Whether this technician is one of them.</param>
    public static IReadOnlyList<Violation> Check(Technician technician, Shift shift, IEnumerable<Shift> technicianShifts, int assignedCount, bool alreadyAssigned)
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

        if (alreadyAssigned)
        {
            violations.Add(new Violation(ViolationCode.AlreadyAssigned, $"{technician.FullName} is already on this shift."));
        }
        else if (assignedCount >= shift.Headcount)
        {
            violations.Add(new Violation(ViolationCode.ShiftFull, $"This shift already has {shift.Headcount} technician(s)."));
        }

        var existing = technicianShifts.Where(s => s.Id != shift.Id).ToList();

        foreach (var other in existing.Where(shift.Overlaps).OrderBy(s => s.StartsAt))
        {
            violations.Add(new Violation(
                ViolationCode.Overlap,
                $"Overlaps with {other.Site?.Name ?? "another shift"} {other.StartsAt:ddd HH:mm}-{other.EndsAt:HH:mm}.",
                other.Id));
        }

        var week = IsoWeek.Containing(shift.StartsAt);
        var scheduled = existing.Where(s => week.Contains(s.StartsAt)).Sum(s => s.Hours);
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
