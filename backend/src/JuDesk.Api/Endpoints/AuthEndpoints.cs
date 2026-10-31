using JuDesk.Api.Auth;
using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Endpoints;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("JuDesk.Auth");
            return await LoginAsync(request, db, hasher, http, logger, ct);
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces<UserDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Public demo: one shared planner account, no password to type. 404 unless Demo:Mode is on.
        group.MapPost("/demo", async (DemoService demo, SetupService setup, HttpContext http, CancellationToken ct) =>
        {
            if (!demo.Enabled)
            {
                return Results.NotFound();
            }
            if (await setup.NeedsSetupAsync(ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Create the admin account on /setup first.");
            }
            var user = await demo.SignInUserAsync(ct) ?? throw new InvalidOperationException("Demo is enabled but no demo user came back.");
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user.ToPrincipal());
            return Results.Ok(user.ToDto());
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces<UserDto>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var id = http.User.UserId();
            var user = id is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(user.ToDto());
        })
        .Produces<UserDto>();

        MapUsers(api);
        return api;
    }

    public const int MaxPasswordLength = 128;

    private static readonly User DummyUser = new() { Email = "nobody@judesk.invalid", DisplayName = "nobody", PasswordHash = string.Empty };
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString("N")));

    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, ILogger logger, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length > 200 || password.Length > MaxPasswordLength)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Verify against a dummy hash when the account does not exist, so the response time does
        // not reveal which emails are registered.
        var verified = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash.Value, password);
        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Failed login from {Address}", http.Connection.RemoteIpAddress);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user.ToPrincipal());
        return Results.Ok(user.ToDto());
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization(CookieAuth.AdminPolicy);

        users.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().OrderBy(u => u.Email).Select(u => u.ToDto()).ToListAsync(ct));

        users.MapPost("/", async (CreateUserRequest request, AppDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var v = new Validation()
                .Require("email", email)
                .Must("email", email.Contains('@'), "Must be an email address.")
                .Require("displayName", request.DisplayName, 120)
                .Must("password", request.Password?.Length >= 8, "Must be at least 8 characters.")
                .Must("password", request.Password?.Length <= MaxPasswordLength, $"Must be at most {MaxPasswordLength} characters.");
            if (v.Problem() is { } problem)
            {
                return problem;
            }
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A user with this email already exists.");
            }

            var user = new User { Email = email, DisplayName = request.DisplayName.Trim(), Role = request.Role, PasswordHash = string.Empty };
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", user.ToDto());
        })
        .Produces<UserDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        users.MapDelete("/{id:guid}", async (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (http.User.UserId() == id)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot delete your own account.");
            }
            var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (target is null)
            {
                return Results.NotFound();
            }
            // Without an admin the app would fall back into setup; keep at least one.
            if (target.Role == UserRole.Admin && await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct) <= 1)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This is the last admin account.");
            }
            var deleted = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}
