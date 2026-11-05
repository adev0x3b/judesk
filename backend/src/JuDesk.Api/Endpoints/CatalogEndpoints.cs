using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Endpoints;

/// <summary>Skills, technicians and sites: the reference data a plan is built from.</summary>
public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalog(this RouteGroupBuilder api)
    {
        MapSkills(api.MapGroup("/skills").WithTags("Skills"));
        MapTechnicians(api.MapGroup("/technicians").WithTags("Technicians"));
        MapSites(api.MapGroup("/sites").WithTags("Sites"));
        return api;
    }

    private static void MapSkills(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Skills.AsNoTracking().OrderBy(s => s.Name).Select(s => s.ToDto()).ToListAsync(ct));

        group.MapPost("/", async (SkillRequest request, AppDbContext db, DemoService demo, CancellationToken ct) =>
        {
            if (await demo.CapAsync(db.Skills, catalog: true, "skills", ct) is { } capped)
            {
                return capped;
            }
            var name = request.Name?.Trim() ?? string.Empty;
            if (new Validation().Require("name", name, 80).Problem() is { } problem)
            {
                return problem;
            }
            if (await db.Skills.AnyAsync(s => s.Name == name, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A skill with this name already exists.");
            }
            var skill = new Skill { Name = name };
            db.Skills.Add(skill);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/skills/{skill.Id}", skill.ToDto());
        }).Produces<SkillDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        // The guards below are check-then-act, so each runs serializable: a shift created between the
        // check and the delete makes one of the two transactions fail and retry.
        group.MapDelete("/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Serializable.RunAsync(db, async () =>
        {
            if (await db.Shifts.AnyAsync(s => s.RequiredSkillId == id, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Shifts still require this skill.");
            }
            var deleted = await db.Skills.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        }, ct));
    }

    private static void MapTechnicians(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            (await db.Technicians.AsNoTracking().Include(t => t.Skills).OrderBy(t => t.FullName).ToListAsync(ct))
                .Select(t => t.ToDto()).ToList());

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var technician = await db.Technicians.AsNoTracking().Include(t => t.Skills).FirstOrDefaultAsync(t => t.Id == id, ct);
            return technician is null ? Results.NotFound() : Results.Ok(technician.ToDto());
        }).Produces<TechnicianDto>();

        group.MapPost("/", async (TechnicianRequest request, AppDbContext db, DemoService demo, CancellationToken ct) =>
        {
            if (await demo.CapAsync(db.Technicians, catalog: true, "technicians", ct) is { } capped)
            {
                return capped;
            }
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            var technician = new Technician { FullName = request.FullName.Trim() };
            await ApplyAsync(technician, request, db, ct);
            db.Technicians.Add(technician);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/technicians/{technician.Id}", technician.ToDto());
        }).Produces<TechnicianDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        // Edits that would break upcoming assignments are refused, like the rules refuse a bad assign:
        // removing a skill a shift needs, deactivating, or cutting the weekly limit below what is booked.
        group.MapPut("/{id:guid}", async (Guid id, TechnicianRequest request, AppDbContext db, InstallationZone zone, CancellationToken ct) =>
        {
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            return await Serializable.RunAsync(db, async () =>
            {
                var technician = await db.Technicians.Include(t => t.Skills).FirstOrDefaultAsync(t => t.Id == id, ct);
                if (technician is null)
                {
                    return Results.NotFound();
                }
                if (await UpcomingConflictAsync(technician, request, db, zone, ct) is { } conflict)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: conflict);
                }
                await ApplyAsync(technician, request, db, ct);
                await db.SaveChangesAsync(ct);
                return Results.Ok(technician.ToDto());
            }, ct);
        }).Produces<TechnicianDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Serializable.RunAsync(db, async () =>
        {
            // Past assignments go with the technician; upcoming ones must be handed over first.
            var now = DateTime.UtcNow;
            if (await db.Assignments.AnyAsync(a => a.TechnicianId == id && a.Shift!.EndsAt > now, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This technician is still assigned to upcoming shifts; unassign them first.");
            }
            var deleted = await db.Technicians.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        }, ct));

        // Why the edit cannot go through, or null when it can.
        static async Task<string?> UpcomingConflictAsync(Technician technician, TechnicianRequest request, AppDbContext db, InstallationZone zone, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var upcoming = await db.Assignments.AsNoTracking()
                .Where(a => a.TechnicianId == technician.Id && a.Shift!.EndsAt > now)
                .Select(a => new { a.Shift!.StartsAt, a.Shift.EndsAt, a.Shift.RequiredSkillId, Skill = a.Shift.RequiredSkill!.Name })
                .ToListAsync(ct);
            if (upcoming.Count == 0)
            {
                return null;
            }
            if (technician.Active && !request.Active)
            {
                return $"{technician.FullName} is on {upcoming.Count} upcoming shift(s); unassign them before deactivating.";
            }
            var kept = request.SkillIds?.ToHashSet() ?? [];
            var stillNeeded = upcoming.Where(u => !kept.Contains(u.RequiredSkillId)).Select(u => u.Skill).Distinct().Order().ToList();
            if (stillNeeded.Count > 0)
            {
                return $"Upcoming shifts still need {string.Join(", ", stillNeeded)}; unassign them before removing the skill.";
            }
            if (request.MaxWeeklyHours < technician.MaxWeeklyHours)
            {
                var busiest = upcoming
                    .GroupBy(u => zone.WeekContaining(u.StartsAt).Monday)
                    .Select(g => (Monday: g.Key, Hours: g.Sum(u => (u.EndsAt - u.StartsAt).TotalHours)))
                    .Where(w => w.Hours > request.MaxWeeklyHours)
                    .OrderBy(w => w.Monday)
                    .FirstOrDefault();
                if (busiest.Hours > 0)
                {
                    return $"{technician.FullName} already has {busiest.Hours:0.#} h in the week of {busiest.Monday:d MMM}; unassign some shifts before lowering the limit to {request.MaxWeeklyHours} h.";
                }
            }
            return null;
        }

        static async Task<IResult?> ValidateAsync(TechnicianRequest request, AppDbContext db, CancellationToken ct)
        {
            var ids = request.SkillIds?.Distinct().ToList() ?? [];
            var known = await db.Skills.CountAsync(s => ids.Contains(s.Id), ct);
            return new Validation()
                .Require("fullName", request.FullName, 120)
                .MaxLength("email", request.Email, 200)
                .MaxLength("phone", request.Phone, 40)
                .Range("maxWeeklyHours", request.MaxWeeklyHours, 1, 80)
                .Must("skillIds", known == ids.Count, "Contains an unknown skill.")
                .Problem();
        }

        static async Task ApplyAsync(Technician technician, TechnicianRequest request, AppDbContext db, CancellationToken ct)
        {
            var ids = request.SkillIds?.Distinct().ToList() ?? [];
            technician.FullName = request.FullName.Trim();
            technician.Email = NullIfBlank(request.Email);
            technician.Phone = NullIfBlank(request.Phone);
            technician.MaxWeeklyHours = request.MaxWeeklyHours;
            technician.Active = request.Active;
            technician.Skills = await db.Skills.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        }
    }

    private static void MapSites(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Sites.AsNoTracking().OrderBy(s => s.Name).Select(s => s.ToDto()).ToListAsync(ct));

        group.MapPost("/", async (SiteRequest request, AppDbContext db, DemoService demo, CancellationToken ct) =>
        {
            if (await demo.CapAsync(db.Sites, catalog: true, "sites", ct) is { } capped)
            {
                return capped;
            }
            if (Validate(request) is { } problem)
            {
                return problem;
            }
            var site = new Site { Name = request.Name.Trim(), Address = NullIfBlank(request.Address), Color = NullIfBlank(request.Color) };
            db.Sites.Add(site);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sites/{site.Id}", site.ToDto());
        }).Produces<SiteDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (Guid id, SiteRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { } problem)
            {
                return problem;
            }
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (site is null)
            {
                return Results.NotFound();
            }
            site.Name = request.Name.Trim();
            site.Address = NullIfBlank(request.Address);
            site.Color = NullIfBlank(request.Color);
            await db.SaveChangesAsync(ct);
            return Results.Ok(site.ToDto());
        }).Produces<SiteDto>().ProducesValidationProblem();

        group.MapDelete("/{id:guid}", (Guid id, AppDbContext db, CancellationToken ct) => Serializable.RunAsync(db, async () =>
        {
            // Deleting a site would silently take its shifts and assignments with it.
            var now = DateTime.UtcNow;
            if (await db.Shifts.AnyAsync(s => s.SiteId == id && s.EndsAt > now, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This site still has upcoming shifts; delete or move them first.");
            }
            var deleted = await db.Sites.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        }, ct));

        static IResult? Validate(SiteRequest request) => new Validation()
            .Require("name", request.Name, 120)
            .MaxLength("address", request.Address, 300)
            .Must("color", request.Color is null or { Length: 0 } || System.Text.RegularExpressions.Regex.IsMatch(request.Color, @"^#[0-9a-fA-F]{6}\z"), "Must be a #rrggbb colour.")
            .Problem();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
