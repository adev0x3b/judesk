using JuDesk.Api.Data;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JuDesk.Api.Services;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Public demo: a "Try the demo" login as a planner, and the fleet data reset on a timer.</summary>
    public bool Mode { get; set; }

    public int ResetMinutes { get; set; } = 60;
}

/// <summary>What the demo timer last did and will do next; one per process.</summary>
public sealed class DemoState
{
    public DateTime? LastResetAt { get; set; }
    public DateTime? NextResetAt { get; set; }
}

/// <summary>
/// The public demo. Visitors sign in as one shared planner account through <see cref="SignInUserAsync"/>
/// (its password is random and never shown), and <see cref="ResetAsync"/> puts the fleet back to the
/// seed. Accounts are kept across resets, so the admin created at setup and the demo planner survive.
/// </summary>
public sealed class DemoService(AppDbContext db, IPasswordHasher<User> hasher, IOptions<DemoOptions> options, DemoState state, ILogger<DemoService> logger)
{
    public const string DemoEmail = "demo@judesk.local";

    public bool Enabled => options.Value.Mode;
    public int ResetMinutes => options.Value.ResetMinutes;
    public DateTime? NextResetAt => state.NextResetAt;

    /// <summary>The shared demo planner, created on first use. Null when the demo is off.</summary>
    public async Task<User?> SignInUserAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return null;
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == DemoEmail, ct);
        if (user is null)
        {
            user = new User { Email = DemoEmail, DisplayName = "Demo planner", Role = UserRole.Planner, PasswordHash = string.Empty };
            user.PasswordHash = hasher.HashPassword(user, Guid.NewGuid().ToString("N"));
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    /// <summary>Drops every technician, site, skill, shift and assignment and seeds the demo fleet again.</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Assignments.ExecuteDeleteAsync(ct);
        await db.Shifts.ExecuteDeleteAsync(ct);
        await db.Technicians.ExecuteDeleteAsync(ct);
        await db.Sites.ExecuteDeleteAsync(ct);
        await db.Skills.ExecuteDeleteAsync(ct);
        await DemoSeeder.SeedAsync(db, new SeedOptions { Demo = true }, logger, ct);
        await tx.CommitAsync(ct);
        state.LastResetAt = DateTime.UtcNow;
        logger.LogInformation("Demo data reset");
    }
}

/// <summary>Resets the demo fleet every <see cref="DemoOptions.ResetMinutes"/> while the demo is on.</summary>
public sealed class DemoResetService(IServiceScopeFactory scopes, IOptions<DemoOptions> options, DemoState state, ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromMinutes(Math.Max(1, options.Value.ResetMinutes));
        using var timer = new PeriodicTimer(period);
        state.NextResetAt = DateTime.UtcNow + period;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DemoService>().ResetAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Demo reset failed; trying again next period");
            }
            state.NextResetAt = DateTime.UtcNow + period;
        }
    }
}
