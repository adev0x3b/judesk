using JuDesk.Domain.Scheduling;

namespace JuDesk.Tests.Unit;

public class IsoWeekTests
{
    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Monday
    [InlineData("2026-10-14", "2026-10-12")] // Wednesday
    [InlineData("2026-10-18", "2026-10-12")] // Sunday
    [InlineData("2026-10-19", "2026-10-19")] // next Monday
    public void Containing_snaps_to_monday(string date, string expectedMonday)
    {
        var week = IsoWeek.Containing(DateOnly.Parse(date));
        Assert.Equal(DateOnly.Parse(expectedMonday), week.Monday);
        Assert.Equal(DateOnly.Parse(expectedMonday).AddDays(6), week.Sunday);
    }

    [Fact]
    public void Bounds_are_utc_and_end_is_exclusive()
    {
        var week = new IsoWeek(new DateOnly(2026, 10, 12));
        Assert.Equal(DateTimeKind.Utc, week.StartUtc.Kind);
        Assert.True(week.Contains(new DateTime(2026, 10, 18, 23, 59, 59, DateTimeKind.Utc)));
        Assert.False(week.Contains(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(week.Next().StartUtc, week.EndUtc);
    }

    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    [Fact]
    public void A_week_in_rome_starts_at_italian_midnight()
    {
        var week = new IsoWeek(new DateOnly(2026, 11, 2), Rome);
        Assert.Equal(new DateTime(2026, 11, 1, 23, 0, 0, DateTimeKind.Utc), week.StartUtc);
        Assert.True(week.Contains(new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Utc)));      // Mon 00:30 in Rome
        Assert.Equal(week.Monday, IsoWeek.Containing(new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Utc), Rome).Monday);
    }

    [Theory]
    [InlineData("2026-03-23", 167)] // clocks go forward on Sunday 29 March
    [InlineData("2026-10-19", 169)] // clocks go back on Sunday 25 October
    [InlineData("2026-11-02", 168)]
    public void Weeks_across_a_clock_change_have_their_real_length(string monday, int hours)
    {
        var week = new IsoWeek(DateOnly.Parse(monday), Rome);
        Assert.Equal(hours, (week.EndUtc - week.StartUtc).TotalHours);
    }

    [Fact]
    public void Wall_clock_times_skipped_or_repeated_by_a_clock_change_resolve_sensibly()
    {
        // 02:30 does not exist on 29 March 2026 in Rome: it moves to 03:00 CEST.
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc), Zoned.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Rome));
        // 02:30 happens twice on 25 October 2026: the first one (CEST) is taken.
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), Zoned.ToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Rome));
    }
}
