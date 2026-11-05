using System.Security.Claims;
using JuDesk.Api.Data;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Auth;

public static class CookieAuth
{
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddJuDeskAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "judesk.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                // An API answers 401/403, it never redirects to a login page.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                // Every request checks the cookie's session row: signed out, idle too long, past its absolute
                // lifetime, user deleted (the row cascades) or role changed all give a clean 401 and a
                // cleared cookie instead of a half-authenticated state. One primary-key lookup per request.
                options.Events.OnValidatePrincipal = async ctx =>
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var sessionId = ctx.Principal is null ? null : Sessions.CurrentId(ctx.Principal);
                    var userId = ctx.Principal?.UserId();
                    var role = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                    var session = sessionId is null
                        ? null
                        : await db.UserSessions.AsNoTracking()
                            .Where(s => s.Id == sessionId)
                            .Select(s => new { s.UserId, s.CreatedAt, s.LastSeenAt, s.User!.Role })
                            .FirstOrDefaultAsync();
                    var now = DateTime.UtcNow;
                    if (session is null
                        || session.UserId != userId
                        || session.Role.ToString() != role
                        || now - session.LastSeenAt > Sessions.IdleTimeout
                        || now - session.CreatedAt > Sessions.AbsoluteLifetime)
                    {
                        if (sessionId is not null)
                        {
                            await db.UserSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
                        }
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }
                    if (now - session.LastSeenAt > Sessions.TouchInterval)
                    {
                        await db.UserSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now));
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(nameof(UserRole.Admin)));

        return services;
    }

    /// <summary>The cookie's claims; <paramref name="sessionToken"/> ties it to a <see cref="UserSession"/> row.</summary>
    public static ClaimsPrincipal ToPrincipal(this User user, string sessionToken)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(Sessions.ClaimType, sessionToken),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
