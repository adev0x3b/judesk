using System.Net;
using JuDesk.Api.Auth;
using JuDesk.Api.Services;
using JuDesk.Domain.Scheduling;
using Microsoft.AspNetCore.Http;
using static JuDesk.Tests.Unit.TestData;

namespace JuDesk.Tests.Unit;

public class AbuseGuardTests
{
    [Fact]
    public void A_flood_of_other_emails_cannot_reset_a_counter()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2031-01-01T00:00:00Z"));
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++)
        {
            throttle.Failed("admin@example.com");
        }

        for (var i = 0; i < 25_000; i++)
        {
            throttle.Failed($"noise{i}@example.com");
        }
        throttle.Failed("admin@example.com");

        Assert.True(throttle.IsLocked("admin@example.com"));
    }

    [Fact]
    public void A_lock_lifts_after_the_lock_period_and_quiet_counters_start_over()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2031-01-01T00:00:00Z"));
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.MaxFailures; i++)
        {
            throttle.Failed("pat@example.com");
        }
        Assert.True(throttle.IsLocked("PAT@example.com "));

        clock.Advance(LoginThrottle.LockFor + TimeSpan.FromSeconds(1));
        Assert.False(throttle.IsLocked("pat@example.com"));

        throttle.Failed("sam@example.com");
        clock.Advance(LoginThrottle.LockFor + TimeSpan.FromSeconds(1));
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++)
        {
            throttle.Failed("sam@example.com");
        }
        Assert.False(throttle.IsLocked("sam@example.com"));
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Rate_limits_count_an_IPv6_network_as_one_client(string address, string key)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(key, RateLimits.ClientKey(http));
    }

    [Fact]
    public void The_planner_stops_when_its_time_is_up()
    {
        var tech = Tech("Ada", skills: Electrical);
        var shift = ShiftAt(0, 8, 4, Electrical);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => AutoScheduler.Plan([shift], [tech], ct: cancelled.Token));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
