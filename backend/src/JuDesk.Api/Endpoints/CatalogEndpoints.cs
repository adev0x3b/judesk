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

        group.MapPost("/", async (SkillRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (new Validation().Require("name", name, 80).Problem() is { } problem)
            {
                return problem;
            }
            if (await db.Skills.AnyAsync(s => s.Name == name, ct))
            {
                return Results.Conflict(new { title = "A skill with this name already exists." });
            }
            var skill = new Skill { Name = name };
            db.Skills.Add(skill);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/skills/{skill.Id}", skill.ToDto());
        }).Produces<SkillDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.Shifts.AnyAsync(s => s.RequiredSkillId == id, ct))
            {
                return Results.Conflict(new { title = "Shifts still require this skill." });
            }
            var deleted = await db.Skills.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
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

        group.MapPost("/", async (TechnicianRequest request, AppDbContext db, CancellationToken ct) =>
        {
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

        group.MapPut("/{id:guid}", async (Guid id, TechnicianRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (await ValidateAsync(request, db, ct) is { } problem)
            {
                return problem;
            }
            var technician = await db.Technicians.Include(t => t.Skills).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (technician is null)
            {
                return Results.NotFound();
            }
            var keptSkills = request.SkillIds?.ToHashSet() ?? [];
            var removedSkillIds = technician.Skills.Select(s => s.Id).Where(s => !keptSkills.Contains(s)).ToList();
            if (removedSkillIds.Count > 0)
            {
                var now = DateTime.UtcNow;
                var stillNeeded = await db.Assignments
                    .Where(a => a.TechnicianId == id && a.Shift!.EndsAt > now && removedSkillIds.Contains(a.Shift.RequiredSkillId))
                    .Select(a => a.Shift!.RequiredSkill!.Name)
                    .Distinct()
                    .ToListAsync(ct);
                if (stillNeeded.Count > 0)
                {
                    return Results.Conflict(new { title = $"Upcoming shifts still need {string.Join(", ", stillNeeded)}; unassign them before removing the skill." });
                }
            }
            await ApplyAsync(technician, request, db, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(technician.ToDto());
        }).Produces<TechnicianDto>().ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Technicians.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

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

        group.MapPost("/", async (SiteRequest request, AppDbContext db, CancellationToken ct) =>
        {
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

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Sites.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        static IResult? Validate(SiteRequest request) => new Validation()
            .Require("name", request.Name, 120)
            .MaxLength("address", request.Address, 300)
            .Must("color", request.Color is null or { Length: 0 } || System.Text.RegularExpressions.Regex.IsMatch(request.Color, "^#[0-9a-fA-F]{6}$"), "Must be a #rrggbb colour.")
            .Problem();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
