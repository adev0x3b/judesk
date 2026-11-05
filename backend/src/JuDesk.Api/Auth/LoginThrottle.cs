using Microsoft.Extensions.Caching.Memory;

namespace JuDesk.Api.Auth;

/// <summary>
/// Per-account brute-force guard, on top of the per-address limit: after <see cref="MaxFailures"/> wrong
/// passwords for one email the account refuses every login for <see cref="LockFor"/>, even with the right
/// password, so a slow password spray from many addresses gets nowhere. Counters are kept per email
/// whether or not the account exists, so the lockout message does not reveal which emails are registered.
/// In memory: the API runs as one instance, and a restart only resets the counters.
/// </summary>
public sealed class LoginThrottle : IDisposable
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    // Bounded, so a flood of random emails evicts old counters instead of growing memory.
    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    private sealed class Entry
    {
        public int Failures;
        public DateTime? LockedUntil;
    }

    public bool IsLocked(string email) =>
        _entries.TryGetValue(Key(email), out Entry? entry) && entry!.LockedUntil > DateTime.UtcNow;

    public void Failed(string email)
    {
        var entry = _entries.GetOrCreate(Key(email), e =>
        {
            e.Size = 1;
            e.SlidingExpiration = LockFor;
            return new Entry();
        })!;
        lock (entry)
        {
            entry.Failures++;
            if (entry.Failures >= MaxFailures)
            {
                entry.Failures = 0;
                entry.LockedUntil = DateTime.UtcNow + LockFor;
            }
        }
    }

    /// <summary>A successful login or a password reset clears the counter.</summary>
    public void Clear(string email) => _entries.Remove(Key(email));

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    public void Dispose() => _entries.Dispose();
}
