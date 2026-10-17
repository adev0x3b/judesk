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
}
