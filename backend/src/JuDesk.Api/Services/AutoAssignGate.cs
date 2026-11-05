namespace JuDesk.Api.Services;

/// <summary>
/// Bounds the auto-scheduler for the whole installation, on top of the per-address limit: at most
/// <c>RateLimits:AutoAssignGlobal</c> plans run at once, nobody queues, and each plan gets
/// <c>RateLimits:AutoAssignSeconds</c> of work (retries included). Planning a week is CPU-bound and
/// grows with shifts times technicians, so without this a few clients on a public demo could keep
/// every core busy.
/// </summary>
public sealed class AutoAssignGate(IConfiguration config)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, config.GetValue("RateLimits:AutoAssignGlobal", 2)));

    public TimeSpan Budget { get; } = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("RateLimits:AutoAssignSeconds", 10)));

    /// <summary>A held slot, or null when every slot is busy.</summary>
    public IDisposable? TryEnter() => _slots.Wait(0) ? new Slot(_slots) : null;

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}

/// <summary>The week did not fit in the auto-scheduler's time budget.</summary>
public sealed class PlanTooLarge() : Exception("The week is too large to plan within the time budget.");
