using JuDesk.Domain.Entities;
using JuDesk.Domain.Scheduling;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Data;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string AdminEmail { get; set; } = "admin@judesk.local";
    public string AdminPassword { get; set; } = "ChangeMe123!";
    public string AdminName { get; set; } = "Admin";
    public bool Demo { get; set; } = true;
}

/// <summary>
/// Creates the first admin user and, when <see cref="SeedOptions.Demo"/> is on, a realistic
/// fleet of technicians, sites and shifts around the current week so the demo has something to show.
/// Idempotent: it only writes when the corresponding tables are empty.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(AppDbContext db, SeedOptions options, IPasswordHasher<User> hasher, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(ct))
        {
            var admin = new User
            {
                Email = options.AdminEmail.Trim().ToLowerInvariant(),
                DisplayName = options.AdminName,
                Role = UserRole.Admin,
                PasswordHash = string.Empty,
            };
            admin.PasswordHash = hasher.HashPassword(admin, options.AdminPassword);
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created admin user {Email}", admin.Email);
        }

        if (!options.Demo || await db.Technicians.AnyAsync(ct))
        {
            return;
        }

        var electrical = new Skill { Name = "Electrical" };
        var hvac = new Skill { Name = "HVAC" };
        var plumbing = new Skill { Name = "Plumbing" };
        var networking = new Skill { Name = "Networking" };
        var fire = new Skill { Name = "Fire safety" };
        db.Skills.AddRange(electrical, hvac, plumbing, networking, fire);

        var technicians = new List<Technician>
        {
            new() { FullName = "Giulia Ferri", Email = "giulia.ferri@judesk.local", MaxWeeklyHours = 40, Skills = [electrical, fire] },
            new() { FullName = "Marco Bianchi", Email = "marco.bianchi@judesk.local", MaxWeeklyHours = 40, Skills = [hvac, plumbing] },
            new() { FullName = "Sara Conti", Email = "sara.conti@judesk.local", MaxWeeklyHours = 32, Skills = [networking, electrical] },
            new() { FullName = "Luca Romano", Email = "luca.romano@judesk.local", MaxWeeklyHours = 40, Skills = [plumbing] },
            new() { FullName = "Elena Greco", Email = "elena.greco@judesk.local", MaxWeeklyHours = 40, Skills = [hvac, fire] },
            new() { FullName = "Davide Marino", Email = "davide.marino@judesk.local", MaxWeeklyHours = 24, Skills = [networking] },
            new() { FullName = "Chiara Rizzo", Email = "chiara.rizzo@judesk.local", MaxWeeklyHours = 40, Skills = [electrical, hvac] },
            new() { FullName = "Andrea Galli", Email = "andrea.galli@judesk.local", MaxWeeklyHours = 40, Skills = [plumbing, fire], Active = false },
        };
        db.Technicians.AddRange(technicians);

        var sites = new List<Site>
        {
            new() { Name = "Ospedale San Rossore", Address = "Via delle Torri 12, Pisa", Color = "#2563eb" },
            new() { Name = "Centro logistico Navacchio", Address = "Via Tosco Romagnola 8, Cascina", Color = "#16a34a" },
            new() { Name = "Campus Le Piagge", Address = "Via Diotisalvi 2, Pisa", Color = "#d97706" },
            new() { Name = "Data center Livorno", Address = "Via Firenze 40, Livorno", Color = "#7c3aed" },
        };
        db.Sites.AddRange(sites);

        var thisWeek = IsoWeek.Containing(DateTime.UtcNow);
        var shifts = new List<Shift>();
        foreach (var week in new[] { thisWeek.Previous(), thisWeek, thisWeek.Next() })
        {
            shifts.AddRange(BuildWeek(week, sites, electrical, hvac, plumbing, networking, fire));
        }
        db.Shifts.AddRange(shifts);

        // Hand-made assignments for the current week so the calendar is partly filled on first open;
        // the planner can then click "Auto-assign" to fill the rest.
        var manual = shifts.Where(s => thisWeek.Contains(s.StartsAt)).OrderBy(s => s.StartsAt).Take(4).ToList();
        foreach (var shift in manual)
        {
            var candidate = technicians.FirstOrDefault(t =>
                t.Active && t.Skills.Any(s => s.Id == shift.RequiredSkillId) && t.Assignments.All(a => !a.Shift!.Overlaps(shift)));
            if (candidate is null)
            {
                continue;
            }

            var assignment = new Assignment { Shift = shift, ShiftId = shift.Id, Technician = candidate, TechnicianId = candidate.Id, Source = AssignmentSource.Manual };
            shift.Assignments.Add(assignment);
            candidate.Assignments.Add(assignment);
            db.Assignments.Add(assignment);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo data: {Technicians} technicians, {Sites} sites, {Shifts} shifts", technicians.Count, sites.Count, shifts.Count);
    }

    private static IEnumerable<Shift> BuildWeek(IsoWeek week, List<Site> sites, Skill electrical, Skill hvac, Skill plumbing, Skill networking, Skill fire)
    {
        Shift At(int site, int day, int startHour, int hours, Skill skill, int headcount = 1, string? notes = null)
        {
            var start = week.StartUtc.AddDays(day).AddHours(startHour);
            return new Shift
            {
                Site = sites[site],
                SiteId = sites[site].Id,
                StartsAt = start,
                EndsAt = start.AddHours(hours),
                RequiredSkill = skill,
                RequiredSkillId = skill.Id,
                Headcount = headcount,
                Notes = notes,
            };
        }

        // Hospital: daily HVAC round plus electrical cover, fire check on Friday.
        for (var day = 0; day < 5; day++)
        {
            yield return At(0, day, 6, 8, hvac, notes: "Morning HVAC round");
            yield return At(0, day, 14, 8, electrical);
        }
        yield return At(0, 4, 9, 4, fire, notes: "Quarterly fire-safety inspection");

        // Logistics centre: plumbing and electrical mid-week, weekend cover.
        yield return At(1, 1, 8, 8, plumbing);
        yield return At(1, 3, 8, 8, plumbing);
        yield return At(1, 2, 8, 6, electrical, headcount: 2, notes: "Conveyor line retrofit");
        yield return At(1, 5, 8, 6, electrical);

        // Campus: networking on three days, HVAC once.
        yield return At(2, 0, 9, 8, networking);
        yield return At(2, 2, 9, 8, networking);
        yield return At(2, 4, 9, 8, networking, notes: "Wi-Fi survey, building B");
        yield return At(2, 3, 7, 5, hvac);

        // Data centre: 24h coverage split in three shifts on two days, networking needs two people.
        foreach (var day in new[] { 1, 4 })
        {
            yield return At(3, day, 0, 8, networking, headcount: 2);
            yield return At(3, day, 8, 8, electrical);
            yield return At(3, day, 16, 8, hvac);
        }
        yield return At(3, 6, 10, 4, fire);
    }
}
