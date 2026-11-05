using JuDesk.Api.Auth;
using JuDesk.Api.Contracts;
using JuDesk.Api.Data;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Endpoints;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    private const string InvalidLogin = "Invalid email or password.";
    private const string LockedLogin = "Too many failed attempts. Try again in a few minutes.";

    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, LoginThrottle throttle, Sessions sessions, HttpContext http, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("JuDesk.Auth");
            return await LoginAsync(request, db, hasher, throttle, sessions, http, logger, ct);
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces<UserDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Public demo: one shared planner account, no password to type. 404 unless Demo:Mode is on.
        group.MapPost("/demo", async (DemoService demo, SetupService setup, Sessions sessions, HttpContext http, CancellationToken ct) =>
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
            await sessions.SignInAsync(http, user, ct);
            return Results.Ok(user.ToDto());
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces<UserDto>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // Ends this session on the server too, so a copy of the cookie stops working.
        group.MapPost("/logout", async (Sessions sessions, HttpContext http, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(http, ct);
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var id = http.User.UserId();
            var user = id is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(user.ToDto());
        })
        .Produces<UserDto>();

        // Change your own password. Every other session of the account ends; this one stays.
        group.MapPost("/password", async (ChangePasswordRequest request, AppDbContext db, IPasswordHasher<User> hasher, Sessions sessions, HttpContext http, CancellationToken ct) =>
        {
            var id = http.User.UserId();
            var user = id is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (user.Email == DemoService.DemoEmail)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The demo account has no password to change.");
            }
            var current = request.CurrentPassword ?? string.Empty;
            var currentOk = current.Length <= PasswordPolicy.MaxLength
                && hasher.VerifyHashedPassword(user, user.PasswordHash, current) != PasswordVerificationResult.Failed;
            var v = new Validation()
                .Must("currentPassword", currentOk, "Wrong password.")
                .Check("newPassword", request.NewPassword, user.Email, user.DisplayName)
                .Must("newPassword", request.NewPassword != current, "Must differ from the current password.");
            if (v.Problem() is { } problem)
            {
                return problem;
            }
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
            await db.SaveChangesAsync(ct);
            await sessions.EndAllAsync(user.Id, keep: Sessions.CurrentId(http.User), ct);
            return Results.NoContent();
        })
        // Guessing the current password through this endpoint is throttled like a login.
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden);

        MapUsers(api);
        return api;
    }

    private static readonly User DummyUser = new() { Email = "nobody@judesk.invalid", DisplayName = "nobody", PasswordHash = string.Empty };
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString("N")));

    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, LoginThrottle throttle, Sessions sessions, HttpContext http, ILogger logger, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length > 200 || password.Length > PasswordPolicy.MaxLength)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidLogin);
        }
        // A locked account refuses even the right password, so a spray cannot confirm a guess.
        if (throttle.IsLocked(email))
        {
            logger.LogWarning("Login to a locked account from {Address}", http.Connection.RemoteIpAddress);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: LockedLogin);
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Verify against a dummy hash when the account does not exist, so the response time does
        // not reveal which emails are registered.
        var verified = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash.Value, password);
        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            throttle.Failed(email);
            logger.LogWarning("Failed login from {Address}", http.Connection.RemoteIpAddress);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: throttle.IsLocked(email) ? LockedLogin : InvalidLogin);
        }
        throttle.Clear(email);

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }

        await sessions.SignInAsync(http, user, ct);
        return Results.Ok(user.ToDto());
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization(CookieAuth.AdminPolicy);

        users.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            (await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct)).Select(u => u.ToDto()).ToList());

        users.MapPost("/", async (CreateUserRequest request, AppDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var v = new Validation()
                .Require("email", email)
                .Must("email", email.Contains('@'), "Must be an email address.")
                .Require("displayName", request.DisplayName, 120)
                .Must("role", Enum.IsDefined(request.Role), "Must be Planner or Admin.")
                .Check("password", request.Password, email, request.DisplayName);
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

        // An admin sets a new password for someone who lost theirs or may have leaked it. All their
        // sessions end and any login lockout is lifted.
        users.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest request, AppDbContext db, IPasswordHasher<User> hasher, Sessions sessions, LoginThrottle throttle, HttpContext http, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }
            if (user.Email == DemoService.DemoEmail)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The demo account has no password to reset.");
            }
            if (new Validation().Check("newPassword", request.NewPassword, user.Email, user.DisplayName).Problem() is { } problem)
            {
                return problem;
            }
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
            await db.SaveChangesAsync(ct);
            await sessions.EndAllAsync(user.Id, keep: Sessions.CurrentId(http.User), ct);
            throttle.Clear(user.Email);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapDelete("/{id:guid}", (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
            Serializable.RunAsync(db, async () =>
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
                // Without an admin the app would fall back into setup; keep at least one. Serializable, so
                // two admins deleting each other at the same moment cannot both get through.
                if (target.Role == UserRole.Admin && await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct) <= 1)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This is the last admin account.");
                }
                var deleted = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
                return deleted > 0 ? Results.NoContent() : Results.NotFound();
            }, ct));
    }
}
