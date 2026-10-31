using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JuDesk.Api.Services;

public static class DbErrors
{
    public static PostgresException? Postgres(Exception e) => e as PostgresException ?? e.InnerException as PostgresException;

    public static bool IsUniqueViolation(Exception e) => Postgres(e)?.SqlState == PostgresErrorCodes.UniqueViolation;

    public static bool IsForeignKeyViolation(Exception e) => Postgres(e)?.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    public static bool IsSerializationFailure(Exception e) =>
        Postgres(e)?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
}

/// <summary>
/// Two requests that race each other (the same name inserted twice, a row deleted under a write,
/// a serializable transaction that lost after its retries) are a conflict, not a server error.
/// </summary>
public sealed class DbConflictHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        string? title = exception switch
        {
            DbUpdateException when DbErrors.IsUniqueViolation(exception) => "Something with the same name or key already exists.",
            DbUpdateException when DbErrors.IsForeignKeyViolation(exception) => "Something this change depends on was just deleted. Reload and try again.",
            _ when DbErrors.IsSerializationFailure(exception) => "Another change got in the way. Try again.",
            _ => null,
        };
        if (title is null)
        {
            return false;
        }
        http.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new() { Status = StatusCodes.Status409Conflict, Title = title },
        });
    }
}
