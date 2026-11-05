using JuDesk.Api.Contracts;
using JuDesk.Api.Services;
using JuDesk.Domain.Scheduling;

namespace JuDesk.Api.Endpoints;

/// <summary>Week-level operations. A week is named by any of its days, in the installation's time zone.</summary>
public static class ScheduleEndpoints
{
    public static RouteGroupBuilder MapSchedule(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/schedule").WithTags("Schedule");

        group.MapGet("/week", async (DateOnly? weekStart, ScheduleService schedule, InstallationZone zone, CancellationToken ct) =>
            BadWeek(weekStart) ? Dates.Problem("weekStart") : Results.Ok(await schedule.GetWeekAsync(WeekOf(weekStart, zone), ct)))
            .Produces<WeekScheduleDto>().ProducesValidationProblem();

        // The planner is CPU work over the whole week, so each address runs only a few at once (RateLimits:HeavyConcurrency).
        group.MapPost("/auto-assign", async (AutoAssignRequest request, ScheduleService schedule, InstallationZone zone, CancellationToken ct) =>
            !Dates.IsSane(request.WeekStart)
                ? Dates.Problem("weekStart")
                : Results.Ok(await schedule.AutoAssignAsync(zone.Week(request.WeekStart), request.DryRun, ct)))
            .RequireRateLimiting(RateLimits.HeavyPolicy)
            .Produces<AutoAssignResultDto>().ProducesValidationProblem();

        group.MapDelete("/auto-assignments", async (DateOnly? weekStart, ScheduleService schedule, InstallationZone zone, CancellationToken ct) =>
            BadWeek(weekStart) ? Dates.Problem("weekStart") : Results.Ok(new { removed = await schedule.ClearAutoAssignmentsAsync(WeekOf(weekStart, zone), ct) }));

        group.MapGet("/export", async (DateOnly? weekStart, ScheduleService schedule, InstallationZone zone, CancellationToken ct) =>
        {
            if (BadWeek(weekStart))
            {
                return Dates.Problem("weekStart");
            }
            var week = WeekOf(weekStart, zone);
            var csv = await schedule.ExportCsvAsync(week, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"judesk-week-{week.Monday:yyyy-MM-dd}.csv");
        }).Produces(StatusCodes.Status200OK, contentType: "text/csv");

        return api;
    }

    private static IsoWeek WeekOf(DateOnly? weekStart, InstallationZone zone) =>
        weekStart is { } d ? zone.Week(d) : zone.ThisWeek();

    private static bool BadWeek(DateOnly? weekStart) => weekStart is { } d && !Dates.IsSane(d);
}
