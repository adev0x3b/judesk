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

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user.ToPrincipal());
            return Results.Ok(user.ToDto());
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .Produces<UserDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

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
                .Must("password", request.Password?.Length >= 8, "Must be at least 8 characters.");
            if (v.Problem() is { } problem)
            {
                return problem;
            }
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                return Results.Conflict(new { title = "A user with this email already exists." });
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
                return Results.Conflict(new { title = "You cannot delete your own account." });
            }
            var deleted = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        return api;
    }
}
