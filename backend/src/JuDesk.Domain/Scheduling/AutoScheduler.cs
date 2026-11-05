using JuDesk.Domain.Entities;

namespace JuDesk.Domain.Scheduling;

public sealed record ProposedAssignment(Guid ShiftId, Guid TechnicianId);

public sealed record UnfilledSlot(Guid ShiftId, int Missing, string Reason);

public sealed record SchedulePlan(IReadOnlyList<ProposedAssignment> Assignments, IReadOnlyList<UnfilledSlot> Unfilled)
{
    public static readonly SchedulePlan Empty = new([], []);
}

/// <summary>
/// Fills the open slots of a week with a constraint-aware greedy heuristic:
/// most constrained shifts first (fewest eligible technicians), each slot going to the eligible
/// technician with the fewest hours already scheduled that week. Every proposal respects
/// <see cref="AssignmentRules"/>, so the plan can be applied without re-validation.
/// Greedy is not optimal, but it is fast, explainable and good enough for a planner to touch up.
/// </summary>
public static class AutoScheduler
{
    /// <param name="weekShifts">The shifts of the week being planned, with their assignments.</param>
    /// <param name="technicians">The technicians to plan with, with their skills.</param>
    /// <param name="surroundingShifts">
    /// Assigned shifts just outside the week (the day before and after). They are never planned,
    /// but a shift crossing midnight on Sunday or Monday must not be double-booked.
    /// </param>
    /// <param name="zone">The installation's time zone, for the weekly-hours rule. UTC when null.</param>
    public static SchedulePlan Plan(IReadOnlyCollection<Shift> weekShifts, IReadOnlyCollection<Technician> technicians, IReadOnlyCollection<Shift>? surroundingShifts = null, TimeZoneInfo? zone = null)
    {
        surroundingShifts ??= [];
        if (weekShifts.Count == 0 || technicians.Count == 0)
        {
            return weekShifts.Count == 0
                ? SchedulePlan.Empty
                : new SchedulePlan([], weekShifts
                    .Where(s => s.Assignments.Count < s.Headcount)
                    .Select(s => new UnfilledSlot(s.Id, s.Headcount - s.Assignments.Count, "No technicians."))
                    .ToList());
        }

        // Working copies so we never mutate the caller's graph while planning.
        var shiftsById = weekShifts.Concat(surroundingShifts).DistinctBy(s => s.Id).ToDictionary(s => s.Id);
        var assignedShiftIds = technicians.ToDictionary(
            t => t.Id,
            t => shiftsById.Values.Where(s => s.Assignments.Any(a => a.TechnicianId == t.Id)).Select(s => s.Id).ToHashSet());
        var currentCount = weekShifts.ToDictionary(s => s.Id, s => s.Assignments.Count);

        var proposals = new List<ProposedAssignment>();
        var unfilled = new List<UnfilledSlot>();

        IReadOnlyList<Violation> CheckFor(Technician t, Shift s) =>
            AssignmentRules.Check(
                t, s,
                assignedShiftIds[t.Id].Select(id => shiftsById[id]),
                currentCount[s.Id],
                assignedShiftIds[t.Id].Contains(s.Id),
                zone);

        double HoursOf(Technician t, IsoWeek week) => assignedShiftIds[t.Id].Select(id => shiftsById[id]).Where(s => week.Contains(s.StartsAt)).Sum(s => s.Hours);

        var open = weekShifts
            .Where(s => s.Assignments.Count < s.Headcount)
            .Select(s => new
            {
                Shift = s,
                Eligible = technicians.Count(t => CheckFor(t, s).Count == 0),
            })
            .OrderBy(x => x.Eligible)
            .ThenBy(x => x.Shift.StartsAt)
            .ThenBy(x => x.Shift.Id)
            .Select(x => x.Shift)
            .ToList();

        foreach (var shift in open)
        {
            while (currentCount[shift.Id] < shift.Headcount)
            {
                var week = IsoWeek.Containing(shift.StartsAt, zone);
                var pick = technicians
                    .Select(t => new { Technician = t, Violations = CheckFor(t, shift) })
                    .Where(x => x.Violations.Count == 0)
                    .OrderBy(x => HoursOf(x.Technician, week))
                    .ThenBy(x => assignedShiftIds[x.Technician.Id].Count)
                    .ThenBy(x => x.Technician.FullName, StringComparer.Ordinal)
                    .ThenBy(x => x.Technician.Id)
                    .Select(x => x.Technician)
                    .FirstOrDefault();

                if (pick is null)
                {
                    unfilled.Add(new UnfilledSlot(
                        shift.Id,
                        shift.Headcount - currentCount[shift.Id],
                        DescribeWhyUnfilled(technicians, shift, CheckFor)));
                    break;
                }

                proposals.Add(new ProposedAssignment(shift.Id, pick.Id));
                assignedShiftIds[pick.Id].Add(shift.Id);
                currentCount[shift.Id]++;
            }
        }

        return new SchedulePlan(proposals, unfilled);
    }

    private static string DescribeWhyUnfilled(
        IEnumerable<Technician> technicians,
        Shift shift,
        Func<Technician, Shift, IReadOnlyList<Violation>> check)
    {
        var skillName = shift.RequiredSkill?.Name ?? "the required skill";
        var qualified = technicians.Where(t => t.Active && t.Skills.Any(s => s.Id == shift.RequiredSkillId)).ToList();
        if (qualified.Count == 0)
        {
            return $"Unfilled: nobody active has {skillName}.";
        }

        var topReason = qualified
            .SelectMany(t => check(t, shift))
            .Select(v => v.Code)
            .Where(c => c is ViolationCode.Overlap or ViolationCode.MaxHoursExceeded)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .Select(g => (ViolationCode?)g.Key)
            .FirstOrDefault();

        return topReason switch
        {
            ViolationCode.Overlap => $"Unfilled: everyone with {skillName} is already on an overlapping shift.",
            ViolationCode.MaxHoursExceeded => $"Unfilled: everyone with {skillName} would exceed their weekly hours.",
            _ => $"Unfilled: no eligible technician with {skillName}.",
        };
    }
}
