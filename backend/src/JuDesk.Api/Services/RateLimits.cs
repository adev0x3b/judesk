namespace JuDesk.Api.Services;

public static class RateLimits
{
    /// <summary>Per-address window over every API call; see RateLimits:RequestsPerMinute.</summary>
    public const string ApiPolicy = "api";

    /// <summary>Concurrency cap for the auto-scheduler; see RateLimits:HeavyConcurrency.</summary>
    public const string HeavyPolicy = "heavy";
}
