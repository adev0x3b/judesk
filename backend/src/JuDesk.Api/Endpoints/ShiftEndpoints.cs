using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Endpoints;

public static class ShiftEndpoints
{
    /// <summary>Longest range GET /api/shifts returns; a week view asks /api/schedule/week instead.</summary>
    public const int MaxRangeDays = 92;
    public const int MaxShiftHours = 24;
    public const int MaxHeadcount = 20;
    public const int MaxNotesLength = 500;

    public static RouteGroupBuilder MapShifts(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/shifts").WithTags("Shifts");

        // from and to are days in the installation's time zone, both inclusive.
        group.MapGet("/", async (DateOnly? from, DateOnly? to, AppDbContext db, InstallationZone zone, CancellationToken ct) =>
        {
            from ??= zone.ThisWeek().Monday;
            if (!Dates.IsSane(from.Value))
            {
                return Dates.Problem("from");
            }
            to ??= from.Value.AddDays(27);
            if (!Dates.IsSane(to.Value))
            {
                return Dates.Problem("to");
            }
            if (to < from || to.Value.DayNumber - from.Value.DayNumber > MaxRangeDays)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = [$"Must be after 'from' and within {MaxRangeDays} days of it."] });
            }
            var start = zone.StartOfDayUtc(from.Value);
            var end = zone.StartOfDayUtc(to.Value.AddDays(1));
            var shifts = await db.Shifts.AsNoTracking().WithDetails()
                .Where(s => s.StartsAt >= start && s.StartsAt < end)
                .OrderBy(s => s.StartsAt).ThenBy(s => s.Site!.Name)
                .ToListAsync(ct);
            return Results.Ok(shifts.Select(s => s.ToDto()).ToList());
        }).Produces<List<ShiftDto>>().ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var shift = await db.Shifts.AsNoTracking().WithDetails().FirstOrDefaultAsync(s => s.Id == id, ct);
            return shift is null ? Results.NotFound() : Results.Ok(shift.ToDto());
        }).Produces<ShiftDto>();

        group.MapPost("/", async (ShiftRequest request, AppDbContext db, DemoService demo, CancellationToken ct) =>
        {
            if (await demo.CapAsync(db.Shifts, catalog: false, "shifts", ct) is { } capped)
            {
                return capped;
            }
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            var shift = new Shift();
            Apply(shift, request);
            db.Shifts.Add(shift);
            await db.SaveChangesAsync(ct);
            var created = await db.Shifts.AsNoTracking().WithDetails().FirstAsync(s => s.Id == shift.Id, ct);
            return Results.Created($"/api/shifts/{shift.Id}", created.ToDto());
        }).Produces<ShiftDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        // Serializable: an assignment made while the headcount is being lowered cannot slip past the check.
        group.MapPut("/{id:guid}", async (Guid id, ShiftRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            return await Serializable.RunAsync(db, async () =>
            {
                var shift = await db.Shifts.Include(s => s.Assignments).FirstOrDefaultAsync(s => s.Id == id, ct);
                if (shift is null)
                {
                    return Results.NotFound();
                }
                if (shift.Assignments.Count > request.Headcount)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"The shift already has {shift.Assignments.Count} technicians; unassign before lowering the headcount.");
                }
                var timeOrSkillChanged = shift.StartsAt != Utc.Normalize(request.StartsAt)
                    || shift.EndsAt != Utc.Normalize(request.EndsAt)
                    || shift.RequiredSkillId != request.RequiredSkillId;
                if (timeOrSkillChanged && shift.Assignments.Count > 0)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Unassign the technicians before changing the time or skill of a shift.");
                }
                Apply(shift, request);
                await db.SaveChangesAsync(ct);
                var updated = await db.Shifts.AsNoTracking().WithDetails().FirstAsync(s => s.Id == id, ct);
                return Results.Ok(updated.ToDto());
            }, ct);
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
            .SaneDate("startsAt", starts)
            .Must("siteId", await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct), "Unknown site.")
            .Must("requiredSkillId", await db.Skills.AnyAsync(s => s.Id == request.RequiredSkillId, ct), "Unknown skill.")
            .Must("endsAt", ends > starts, "Must be after the start.")
            .Must("endsAt", ends - starts <= TimeSpan.FromHours(MaxShiftHours), $"A shift cannot last more than {MaxShiftHours} hours.")
            .Range("headcount", request.Headcount, 1, MaxHeadcount)
            .MaxLength("notes", request.Notes, MaxNotesLength)
            .Problem();
    }
}
