using System.Globalization;
using System.Text;
using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Services;

public sealed class AssignmentRejected(IReadOnlyList<Violation> violations) : Exception("Assignment violates scheduling rules.")
{
    public IReadOnlyList<Violation> Violations { get; } = violations;
}

/// <summary>
/// Everything that reads or changes a week of assignments. Rules live in the Domain project;
/// this class loads the right slice of data and applies them inside a transaction.
/// </summary>
public sealed class ScheduleService(AppDbContext db)
{
    public async Task<WeekScheduleDto> GetWeekAsync(IsoWeek week, CancellationToken ct)
    {
        var shifts = await LoadWeekShiftsAsync(week, ct);
        var technicians = await db.Technicians.AsNoTracking().Include(t => t.Skills).OrderBy(t => t.FullName).ToListAsync(ct);
        var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

        var hoursByTech = shifts
            .SelectMany(s => s.Assignments.Select(a => (a.TechnicianId, s.Hours)))
            .GroupBy(x => x.TechnicianId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

        var loads = technicians.Select(t => new TechnicianLoadDto(
            t.Id, t.FullName, t.Active, t.MaxWeeklyHours,
            Math.Round(hoursByTech.GetValueOrDefault(t.Id), 2),
            t.Skills.OrderBy(s => s.Name).Select(s => s.Name).ToList())).ToList();

        return new WeekScheduleDto(
            week.Monday,
            week.Sunday,
            sites.Select(s => s.ToDto()).ToList(),
            shifts.Select(s => s.ToDto()).ToList(),
            loads,
            shifts.Sum(s => Math.Max(0, s.Headcount - s.Assignments.Count)));
    }

    public async Task<IReadOnlyList<CandidateDto>?> GetCandidatesAsync(Guid shiftId, CancellationToken ct)
    {
        var shift = await LoadShiftAsync(shiftId, tracking: false, ct);
        if (shift is null)
        {
            return null;
        }

        var week = IsoWeek.Containing(shift.StartsAt);
        var technicians = await db.Technicians.AsNoTracking().Include(t => t.Skills).OrderBy(t => t.FullName).ThenBy(t => t.Id).ToListAsync(ct);
        var around = await LoadShiftsAroundAsync(week, ct);

        return technicians.Select(t =>
        {
            var mine = around.Where(s => s.Assignments.Any(a => a.TechnicianId == t.Id)).ToList();
            var violations = AssignmentRules.Check(t, shift, mine);
            return new CandidateDto(
                t.Id, t.FullName,
                Math.Round(mine.Where(s => s.Id != shift.Id && week.Contains(s.StartsAt)).Sum(s => s.Hours), 2),
                t.MaxWeeklyHours,
                violations.Count == 0,
                violations.Select(v => v.ToDto()).ToList());
        })
        .OrderByDescending(c => c.Eligible)
        .ThenBy(c => c.ScheduledHours)
        .ThenBy(c => c.FullName)
        .ToList();
    }

    /// <summary>
    /// Assigns a technician to a shift after checking every rule. Hard rules (skill, overlap, full,
    /// inactive) can never be overridden; the weekly-hours limit can, when the planner says so.
    /// Runs serializable so two planners cannot overfill a slot at the same time.
    /// </summary>
    public Task<AssignmentDto?> AssignAsync(Guid shiftId, Guid technicianId, bool overrideHours, CancellationToken ct) => Serializable.RunAsync(db, async () =>
    {
        var shift = await LoadShiftAsync(shiftId, tracking: true, ct);
        var technician = await db.Technicians.Include(t => t.Skills).FirstOrDefaultAsync(t => t.Id == technicianId, ct);
        if (shift is null || technician is null)
        {
            return null;
        }

        var week = IsoWeek.Containing(shift.StartsAt);
        var mine = await db.Shifts.AsNoTracking()
            .Include(s => s.Site)
            .Where(AroundWeek(week))
            .Where(s => s.Assignments.Any(a => a.TechnicianId == technicianId))
            .ToListAsync(ct);

        var violations = AssignmentRules.Check(technician, shift, mine)
            .Where(v => !(overrideHours && v.Code == ViolationCode.MaxHoursExceeded))
            .ToList();
        if (violations.Count > 0)
        {
            throw new AssignmentRejected(violations);
        }

        var assignment = new Assignment { ShiftId = shift.Id, TechnicianId = technician.Id, Technician = technician, Source = AssignmentSource.Manual };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return (AssignmentDto?)assignment.ToDto();
    }, ct);

    public async Task<bool> UnassignAsync(Guid shiftId, Guid assignmentId, CancellationToken ct)
    {
        var deleted = await db.Assignments
            .Where(a => a.Id == assignmentId && a.ShiftId == shiftId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public Task<AutoAssignResultDto> AutoAssignAsync(IsoWeek week, bool dryRun, CancellationToken ct) => Serializable.RunAsync(db, async () =>
    {
        var shifts = await LoadWeekShiftsAsync(week, ct);
        var surrounding = (await LoadShiftsAroundAsync(week, ct)).Where(s => !week.Contains(s.StartsAt)).ToList();
        var technicians = await db.Technicians.AsNoTracking().Include(t => t.Skills).Where(t => t.Active).OrderBy(t => t.FullName).ThenBy(t => t.Id).ToListAsync(ct);
        var plan = AutoScheduler.Plan(shifts, technicians, surrounding);
        var names = technicians.ToDictionary(t => t.Id, t => t.FullName);

        if (!dryRun && plan.Assignments.Count > 0)
        {
            db.Assignments.AddRange(plan.Assignments.Select(p => new Assignment
            {
                ShiftId = p.ShiftId,
                TechnicianId = p.TechnicianId,
                Source = AssignmentSource.Auto,
            }));
            await db.SaveChangesAsync(ct);
        }

        return new AutoAssignResultDto(
            dryRun ? 0 : plan.Assignments.Count,
            plan.Assignments.Select(p => new AssignmentPlanDto(p.ShiftId, p.TechnicianId, names[p.TechnicianId])).ToList(),
            plan.Unfilled.Select(u => new UnfilledSlotDto(u.ShiftId, u.Missing, u.Reason)).ToList(),
            dryRun);
    }, ct);

    /// <summary>Removes every auto-generated assignment of the week; manual ones stay.</summary>
    public Task<int> ClearAutoAssignmentsAsync(IsoWeek week, CancellationToken ct) =>
        db.Assignments
            .Where(a => a.Source == AssignmentSource.Auto)
            .Where(a => a.Shift!.StartsAt >= week.StartUtc && a.Shift.StartsAt < week.EndUtc)
            .ExecuteDeleteAsync(ct);

    public async Task<string> ExportCsvAsync(IsoWeek week, CancellationToken ct)
    {
        var shifts = await LoadWeekShiftsAsync(week, ct);
        var sb = new StringBuilder();
        sb.AppendLine("date,start,end,hours,site,skill,technician,source,notes");

        foreach (var shift in shifts.OrderBy(s => s.StartsAt).ThenBy(s => s.Site!.Name))
        {
            var rows = shift.Assignments
                .OrderBy(a => a.Technician!.FullName)
                .Select(a => (Technician: a.Technician!.FullName, Source: a.Source.ToString()))
                .ToList();
            for (var i = rows.Count; i < shift.Headcount; i++)
            {
                rows.Add((Technician: string.Empty, Source: "Unfilled"));
            }

            foreach (var row in rows)
            {
                sb.Append(shift.StartsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                  .Append(shift.StartsAt.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',')
                  .Append(shift.EndsAt.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',')
                  .Append(shift.Hours.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(shift.Site!.Name)).Append(',')
                  .Append(Csv(shift.RequiredSkill!.Name)).Append(',')
                  .Append(Csv(row.Technician)).Append(',')
                  .Append(row.Source).Append(',')
                  .Append(Csv(shift.Notes ?? string.Empty))
                  .AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes a field when needed and neutralises spreadsheet formula injection: a cell starting
    /// with =, +, -, @ or a tab would be evaluated by Excel and LibreOffice, so it gets a leading quote.
    /// </summary>
    internal static string Csv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }
        if (value.IndexOfAny(['"', ',', '\n', '\r']) < 0)
        {
            return value;
        }
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    /// <summary>Shifts that touch the week or the day on either side: enough to catch overlaps across Monday midnight.</summary>
    private static System.Linq.Expressions.Expression<Func<Shift, bool>> AroundWeek(IsoWeek week)
    {
        var from = week.StartUtc.AddDays(-1);
        var to = week.EndUtc.AddDays(1);
        return s => s.StartsAt < to && s.EndsAt > from;
    }

    private Task<List<Shift>> LoadShiftsAroundAsync(IsoWeek week, CancellationToken ct) =>
        db.Shifts.AsNoTracking()
            .Include(s => s.Site)
            .Include(s => s.Assignments)
            .Where(AroundWeek(week))
            .ToListAsync(ct);

    private Task<List<Shift>> LoadWeekShiftsAsync(IsoWeek week, CancellationToken ct) =>
        db.Shifts.AsNoTracking()
            .Include(s => s.Site)
            .Include(s => s.RequiredSkill)
            .Include(s => s.Assignments).ThenInclude(a => a.Technician)
            .Where(s => s.StartsAt >= week.StartUtc && s.StartsAt < week.EndUtc)
            .OrderBy(s => s.StartsAt).ThenBy(s => s.Site!.Name)
            .ToListAsync(ct);

    private Task<Shift?> LoadShiftAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<Shift> query = db.Shifts
            .Include(s => s.Site)
            .Include(s => s.RequiredSkill)
            .Include(s => s.Assignments).ThenInclude(a => a.Technician);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }
        return query.FirstOrDefaultAsync(s => s.Id == id, ct);
    }
}
