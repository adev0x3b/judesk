using System.Data;
using JuDesk.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JuDesk.Api.Services;

/// <summary>
/// Runs a unit of work in a serializable transaction. PostgreSQL aborts one of two concurrent
/// serializable transactions that touch the same rows (SQLSTATE 40001); that is expected, so the
/// work is retried a few times before the error surfaces.
/// </summary>
public static class Serializable
{
    private const int Attempts = 3;

    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var result = await work();
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception e) when (attempt < Attempts && IsSerializationFailure(e))
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsSerializationFailure(Exception e) =>
        (e as PostgresException ?? e.InnerException as PostgresException)?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
}
