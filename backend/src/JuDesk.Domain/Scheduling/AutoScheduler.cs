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
    public static SchedulePlan Plan(IReadOnlyCollection<Shift> weekShifts, IReadOnlyCollection<Technician> technicians)
    {
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
        var shiftsById = weekShifts.ToDictionary(s => s.Id);
        var assignedShiftIds = technicians.ToDictionary(
            t => t.Id,
            t => weekShifts.Where(s => s.Assignments.Any(a => a.TechnicianId == t.Id)).Select(s => s.Id).ToHashSet());
        var currentCount = weekShifts.ToDictionary(s => s.Id, s => s.Assignments.Count);

        var proposals = new List<ProposedAssignment>();
        var unfilled = new List<UnfilledSlot>();

        IReadOnlyList<Violation> CheckFor(Technician t, Shift s)
        {
            var weekShiftsOfTech = assignedShiftIds[t.Id].Select(id => shiftsById[id]);
            var snapshot = SnapshotWithCount(s, currentCount[s.Id], t.Id, assignedShiftIds[t.Id].Contains(s.Id));
            return AssignmentRules.Check(t, snapshot, weekShiftsOfTech);
        }

        double HoursOf(Technician t) => assignedShiftIds[t.Id].Sum(id => shiftsById[id].Hours);

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
                var pick = technicians
                    .Select(t => new { Technician = t, Violations = CheckFor(t, shift) })
                    .Where(x => x.Violations.Count == 0)
                    .OrderBy(x => HoursOf(x.Technician))
                    .ThenBy(x => assignedShiftIds[x.Technician.Id].Count)
                    .ThenBy(x => x.Technician.FullName, StringComparer.Ordinal)
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

    private static Shift SnapshotWithCount(Shift shift, int count, Guid technicianId, bool alreadyAssigned)
    {
        var assignments = Enumerable.Range(0, count)
            .Select(i => new Assignment
            {
                ShiftId = shift.Id,
                // Only the candidate's own id matters to the rules; other slots get placeholder ids.
                TechnicianId = alreadyAssigned && i == 0 ? technicianId : Guid.Empty,
            })
            .ToList();

        return new Shift
        {
            Id = shift.Id,
            SiteId = shift.SiteId,
            Site = shift.Site,
            StartsAt = shift.StartsAt,
            EndsAt = shift.EndsAt,
            RequiredSkillId = shift.RequiredSkillId,
            RequiredSkill = shift.RequiredSkill,
            Headcount = shift.Headcount,
            Assignments = assignments,
        };
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
