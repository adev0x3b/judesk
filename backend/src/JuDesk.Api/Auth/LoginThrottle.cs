using System.Collections.Concurrent;

namespace JuDesk.Api.Auth;

/// <summary>
/// Per-account brute-force guard, on top of the per-address limit: after <see cref="MaxFailures"/> wrong
/// passwords for one email the account refuses every login for <see cref="LockFor"/>, even with the right
/// password, so a slow password spray from many addresses gets nowhere. Counters are kept per email
/// whether or not the account exists, so the lockout message does not reveal which emails are registered.
/// In memory: the API runs as one instance, and a restart only resets the counters.
/// </summary>
/// <remarks>
/// Memory is bounded by pruning counters that went quiet, never by dropping a live one: a flood of
/// random emails must not be able to push the admin's counter out and reset it.
/// </remarks>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    private const int PruneAbove = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    public bool IsLocked(string email) =>
        _entries.TryGetValue(Key(email), out var entry) && Locked(entry, clock.GetUtcNow());

    public void Failed(string email)
    {
        var now = clock.GetUtcNow();
        if (_entries.Count > PruneAbove)
        {
            Prune(now);
        }
        var entry = _entries.GetOrAdd(Key(email), _ => new Entry());
        lock (entry)
        {
            // A counter that went quiet for the lock period starts again from zero.
            if (now - entry.LastFailure > LockFor && !Locked(entry, now))
            {
                entry.Failures = 0;
            }
            entry.LastFailure = now;
            entry.Failures++;
            if (entry.Failures >= MaxFailures)
            {
                entry.Failures = 0;
                entry.LockedUntil = now + LockFor;
            }
        }
    }

    /// <summary>A successful login or a password reset clears the counter.</summary>
    public void Clear(string email) => _entries.TryRemove(Key(email), out _);

    private static bool Locked(Entry entry, DateTimeOffset now) => entry.LockedUntil > now;

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            if (now - entry.LastFailure > LockFor && !Locked(entry, now))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private static string Key(string email) => email.Trim().ToLowerInvariant();
}
