using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JuDesk.Api.Data;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Auth;

/// <summary>
/// Server-side sessions behind the auth cookie (see <see cref="UserSession"/>). The cookie alone would
/// stay valid until it expires; with a row per session, signing out, a password change, an admin reset
/// or deleting the user ends the session on the next request.
/// </summary>
public sealed class Sessions(AppDbContext db)
{
    public const string ClaimType = "judesk:sid";

    /// <summary>A session unused for this long is over (the cookie's own sliding lifetime matches it).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);

    /// <summary>However active, a session ends this long after sign-in.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    /// <summary>LastSeenAt is written at most this often, not on every request.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public async Task SignInAsync(HttpContext http, User user, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        // Old sessions of this user go when a new one starts: no background job needed to keep the table small.
        var idleBefore = now - IdleTimeout;
        var createdBefore = now - AbsoluteLifetime;
        await db.UserSessions
            .Where(s => s.UserId == user.Id && (s.LastSeenAt < idleBefore || s.CreatedAt < createdBefore))
            .ExecuteDeleteAsync(ct);
        db.UserSessions.Add(new UserSession { Id = Hash(token), UserId = user.Id, CreatedAt = now, LastSeenAt = now });
        await db.SaveChangesAsync(ct);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user.ToPrincipal(token));
    }

    public async Task SignOutAsync(HttpContext http, CancellationToken ct = default)
    {
        if (CurrentId(http.User) is { } id)
        {
            await db.UserSessions.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        }
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>Ends every session of the user except <paramref name="keep"/> (the caller's own, when changing their password).</summary>
    public Task<int> EndAllAsync(Guid userId, string? keep, CancellationToken ct = default) =>
        db.UserSessions.Where(s => s.UserId == userId && s.Id != keep).ExecuteDeleteAsync(ct);

    /// <summary>The stored id (token hash) of the session a principal belongs to.</summary>
    public static string? CurrentId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimType) is { Length: > 0 } token ? Hash(token) : null;

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
