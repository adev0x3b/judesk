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
    /// <summary>No usable code: none issued yet, or burnt by too many wrong guesses. Regenerate one.</summary>
    NoCode,
}

/// <summary>
/// First-run bootstrap. While the app has no users it is "in setup": every API start issues a fresh
/// one-time code and prints it to the log, and whoever enters it on the setup page creates the admin
/// account. Only a SHA-256 hash of the code is stored; after <see cref="SetupCode.MaxFailedAttempts"/>
/// wrong guesses the code is burnt and a new one has to be issued (restart, or
/// <c>scripts/regen-setup-code.sh</c>). No account or password ever comes from configuration or a seed.
/// </summary>
public sealed class SetupService(AppDbContext db, IPasswordHasher<User> hasher, ILogger<SetupService> logger)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    /// <summary>Setup is pending until an admin exists; a demo planner alone does not count.</summary>
    public async Task<bool> NeedsSetupAsync(CancellationToken ct = default) => !await db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct);

    /// <summary>Issues a fresh code if the app still needs setup; null once setup is done.</summary>
    public async Task<string?> IssueCodeIfNeededAsync(CancellationToken ct = default) =>
        await NeedsSetupAsync(ct) ? await IssueCodeAsync(ct) : null;

    /// <summary>Replaces any previous code with a fresh one and returns it in clear, once.</summary>
    public async Task<string> IssueCodeAsync(CancellationToken ct = default)
    {
        var code = NewCode();
        var row = await db.SetupCodes.SingleOrDefaultAsync(c => c.Id == SetupCode.SingletonId, ct);
        if (row is null)
        {
            db.SetupCodes.Add(new SetupCode { CodeHash = Hash(code) });
        }
        else
        {
            row.CodeHash = Hash(code);
            row.FailedAttempts = 0;
            row.CreatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return code;
    }

    public Task<(SetupResult Result, User? Admin)> CompleteAsync(string code, string email, string displayName, string password, CancellationToken ct = default) =>
        Serializable.RunAsync(db, async () =>
        {
            if (!await NeedsSetupAsync(ct))
            {
                return (SetupResult.AlreadyDone, (User?)null);
            }

            var row = await db.SetupCodes.SingleOrDefaultAsync(c => c.Id == SetupCode.SingletonId, ct);
            if (row is null)
            {
                return (SetupResult.NoCode, null);
            }

            if (!FixedTimeEquals(row.CodeHash, Hash(code)))
            {
                row.FailedAttempts++;
                if (row.FailedAttempts >= SetupCode.MaxFailedAttempts)
                {
                    db.SetupCodes.Remove(row);
                    logger.LogWarning("Setup code burnt after {Attempts} wrong guesses; issue a new one with --regen-setup-code", row.FailedAttempts);
                }
                else
                {
                    logger.LogWarning("Setup attempt with a wrong code ({Attempts}/{Max})", row.FailedAttempts, SetupCode.MaxFailedAttempts);
                }
                await db.SaveChangesAsync(ct);
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
            logger.LogInformation("Setup completed: admin account created, setup code retired");
            return (SetupResult.Completed, admin);
        }, ct);

    /// <summary>The block printed to the log so `docker compose logs api` shows the code.</summary>
    public static string Banner(string code) => $"""

        ==========================================================
          JuDesk needs its first admin account.
          Open the app, go to /setup and enter this code:

              {code}

          The code changes on every restart.
          A new one now: scripts/regen-setup-code.sh
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

    internal static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    private static string Normalize(string code) => code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
