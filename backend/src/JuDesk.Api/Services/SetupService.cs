using System.Security.Cryptography;
using System.Text;
using JuDesk.Api.Data;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Services;

public enum SetupResult
{
    Completed,
    AlreadyDone,
    WrongCode,
}

/// <summary>
/// First-run bootstrap. While the app has no users it is "in setup": a one-time code is issued,
/// printed to the API log on every start, and whoever enters it on the setup page creates the admin
/// account. No account or password ever comes from configuration or a seed.
/// </summary>
public sealed class SetupService(AppDbContext db, IPasswordHasher<User> hasher, ILogger<SetupService> logger)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    public async Task<bool> NeedsSetupAsync(CancellationToken ct = default) => !await db.Users.AnyAsync(ct);

    /// <summary>Returns the current code, issuing one if there is none. Null once setup is done.</summary>
    public async Task<string?> EnsureCodeAsync(CancellationToken ct = default)
    {
        if (!await NeedsSetupAsync(ct))
        {
            return null;
        }
        var existing = await db.SetupCodes.FirstOrDefaultAsync(ct);
        return existing?.Code ?? await IssueCodeAsync(ct);
    }

    /// <summary>Replaces any previous code with a fresh one and returns it.</summary>
    public async Task<string> IssueCodeAsync(CancellationToken ct = default)
    {
        var code = NewCode();
        var row = await db.SetupCodes.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.SetupCodes.Add(new SetupCode { Code = code });
        }
        else
        {
            row.Code = code;
            row.CreatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<(SetupResult Result, User? Admin)> CompleteAsync(string code, string email, string displayName, string password, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        if (!await NeedsSetupAsync(ct))
        {
            return (SetupResult.AlreadyDone, null);
        }

        var row = await db.SetupCodes.FirstOrDefaultAsync(ct);
        if (row is null || !FixedTimeEquals(Normalize(row.Code), Normalize(code)))
        {
            logger.LogWarning("Setup attempt with a wrong code");
            return (SetupResult.WrongCode, null);
        }

        var admin = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            Role = UserRole.Admin,
            PasswordHash = string.Empty,
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        db.SetupCodes.Remove(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Setup completed: admin {Email} created, setup code retired", admin.Email);
        return (SetupResult.Completed, admin);
    }

    /// <summary>The block printed to the log so `docker compose up` shows the code.</summary>
    public static string Banner(string code) => $"""

        ==========================================================
          JuDesk needs its first admin account.
          Open the app, go to /setup and enter this code:

              {code}

          A new code: scripts/regen-setup-code.sh
        ==========================================================

        """;

    internal static string NewCode()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    private static string Normalize(string code) => code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
