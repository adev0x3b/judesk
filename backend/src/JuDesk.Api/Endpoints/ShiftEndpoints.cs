using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Endpoints;

public static class ShiftEndpoints
{
    public static RouteGroupBuilder MapShifts(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/shifts").WithTags("Shifts");

        group.MapGet("/", async (DateOnly? from, DateOnly? to, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Shifts.AsNoTracking()
                .Include(s => s.Site)
                .Include(s => s.RequiredSkill)
                .Include(s => s.Assignments).ThenInclude(a => a.Technician)
                .AsQueryable();
            if (from is { } f)
            {
                var start = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                query = query.Where(s => s.StartsAt >= start);
            }
            if (to is { } t)
            {
                var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                query = query.Where(s => s.StartsAt < end);
            }
            var shifts = await query.OrderBy(s => s.StartsAt).ThenBy(s => s.Site!.Name).ToListAsync(ct);
            return shifts.Select(s => s.ToDto()).ToList();
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var shift = await Load(db).FirstOrDefaultAsync(s => s.Id == id, ct);
            return shift is null ? Results.NotFound() : Results.Ok(shift.ToDto());
        }).Produces<ShiftDto>();

        group.MapPost("/", async (ShiftRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            var shift = new Shift();
            Apply(shift, request);
            db.Shifts.Add(shift);
            await db.SaveChangesAsync(ct);
            var created = await Load(db).FirstAsync(s => s.Id == shift.Id, ct);
            return Results.Created($"/api/shifts/{shift.Id}", created.ToDto());
        }).Produces<ShiftDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (Guid id, ShiftRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            var shift = await db.Shifts.Include(s => s.Assignments).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (shift is null)
            {
                return Results.NotFound();
            }
            if (shift.Assignments.Count > request.Headcount)
            {
                return Results.Conflict(new { title = $"The shift already has {shift.Assignments.Count} technicians; unassign before lowering the headcount." });
            }
            var timeOrSkillChanged = shift.StartsAt != Utc.Normalize(request.StartsAt)
                || shift.EndsAt != Utc.Normalize(request.EndsAt)
                || shift.RequiredSkillId != request.RequiredSkillId;
            if (timeOrSkillChanged && shift.Assignments.Count > 0)
            {
                return Results.Conflict(new { title = "Unassign the technicians before changing the time or skill of a shift." });
            }
            Apply(shift, request);
            await db.SaveChangesAsync(ct);
            var updated = await Load(db).FirstAsync(s => s.Id == id, ct);
            return Results.Ok(updated.ToDto());
        }).Produces<ShiftDto>().ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Shifts.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/{id:guid}/candidates", async (Guid id, ScheduleService schedule, CancellationToken ct) =>
        {
            var candidates = await schedule.GetCandidatesAsync(id, ct);
            return candidates is null ? Results.NotFound() : Results.Ok(candidates);
        }).Produces<IReadOnlyList<CandidateDto>>();

        group.MapPost("/{id:guid}/assignments", async (Guid id, AssignRequest request, ScheduleService schedule, CancellationToken ct) =>
        {
            try
            {
                var assignment = await schedule.AssignAsync(id, request.TechnicianId, request.OverrideHours, ct);
                return assignment is null
                    ? Results.NotFound()
                    : Results.Created($"/api/shifts/{id}/assignments/{assignment.Id}", assignment);
            }
            catch (AssignmentRejected rejected)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The technician cannot take this shift.",
                    detail: string.Join(" ", rejected.Violations.Select(v => v.Message)),
                    extensions: new Dictionary<string, object?> { ["violations"] = rejected.Violations.Select(v => v.ToDto()).ToList() });
            }
        }).Produces<AssignmentDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/assignments/{assignmentId:guid}", async (Guid id, Guid assignmentId, ScheduleService schedule, CancellationToken ct) =>
            await schedule.UnassignAsync(id, assignmentId, ct) ? Results.NoContent() : Results.NotFound());

        return api;
    }

    private static IQueryable<Shift> Load(AppDbContext db) => db.Shifts.AsNoTracking()
        .Include(s => s.Site)
        .Include(s => s.RequiredSkill)
        .Include(s => s.Assignments).ThenInclude(a => a.Technician);

    private static void Apply(Shift shift, ShiftRequest request)
    {
        shift.SiteId = request.SiteId;
        shift.StartsAt = Utc.Normalize(request.StartsAt);
        shift.EndsAt = Utc.Normalize(request.EndsAt);
        shift.RequiredSkillId = request.RequiredSkillId;
        shift.Headcount = request.Headcount;
        shift.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private static async Task<IResult?> ValidateAsync(ShiftRequest request, AppDbContext db, CancellationToken ct)
    {
        var starts = Utc.Normalize(request.StartsAt);
        var ends = Utc.Normalize(request.EndsAt);
        return new Validation()
            .Must("siteId", await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct), "Unknown site.")
            .Must("requiredSkillId", await db.Skills.AnyAsync(s => s.Id == request.RequiredSkillId, ct), "Unknown skill.")
            .Must("endsAt", ends > starts, "Must be after the start.")
            .Must("endsAt", ends - starts <= TimeSpan.FromHours(24), "A shift cannot last more than 24 hours.")
            .Range("headcount", request.Headcount, 1, 20)
            .MaxLength("notes", request.Notes, 500)
            .Problem();
    }
}

public static class ScheduleEndpoints
{
    public static RouteGroupBuilder MapSchedule(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/schedule").WithTags("Schedule");

        group.MapGet("/week", async (DateOnly? weekStart, ScheduleService schedule, CancellationToken ct) =>
            await schedule.GetWeekAsync(WeekOf(weekStart), ct))
            .Produces<WeekScheduleDto>();

        group.MapPost("/auto-assign", async (AutoAssignRequest request, ScheduleService schedule, CancellationToken ct) =>
            await schedule.AutoAssignAsync(IsoWeek.Containing(request.WeekStart), request.DryRun, ct))
            .Produces<AutoAssignResultDto>();

        group.MapDelete("/auto-assignments", async (DateOnly? weekStart, ScheduleService schedule, CancellationToken ct) =>
            Results.Ok(new { removed = await schedule.ClearAutoAssignmentsAsync(WeekOf(weekStart), ct) }));

        group.MapGet("/export", async (DateOnly? weekStart, ScheduleService schedule, CancellationToken ct) =>
        {
            var week = WeekOf(weekStart);
            var csv = await schedule.ExportCsvAsync(week, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"judesk-week-{week.Monday:yyyy-MM-dd}.csv");
        }).Produces(StatusCodes.Status200OK, contentType: "text/csv");

        return api;
    }

    private static IsoWeek WeekOf(DateOnly? weekStart) =>
        weekStart is { } d ? IsoWeek.Containing(d) : IsoWeek.Containing(DateTime.UtcNow);
}
