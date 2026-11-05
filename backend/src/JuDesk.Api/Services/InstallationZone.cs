using JuDesk.Domain.Scheduling;

namespace JuDesk.Api.Services;

public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    /// <summary>IANA id of the time zone the team works in, e.g. Europe/Rome. Weeks start at its Monday midnight.</summary>
    public string TimeZone { get; set; } = "Europe/Rome";
}

/// <summary>
/// The installation's time zone. Shifts are stored as UTC instants; weeks, days and the CSV export are
/// read in this zone, so a planner in Pisa types and sees Italian wall-clock times.
/// </summary>
public sealed class InstallationZone
{
    public InstallationZone(string timeZoneId)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            throw new InvalidOperationException($"Unknown time zone '{timeZoneId}' in Schedule:TimeZone (TIME_ZONE). Use an IANA id such as Europe/Rome.");
        }
        Zone = zone;
    }

    public TimeZoneInfo Zone { get; }
    public string Id => Zone.Id;

    public IsoWeek Week(DateOnly anyDay) => IsoWeek.Containing(anyDay, Zone);
    public IsoWeek WeekContaining(DateTime utc) => IsoWeek.Containing(utc, Zone);
    public IsoWeek ThisWeek() => WeekContaining(DateTime.UtcNow);
    public DateOnly Today() => Zoned.LocalDate(DateTime.UtcNow, Zone);
    public DateTime StartOfDayUtc(DateOnly date) => Zoned.StartOfDayUtc(date, Zone);
    public DateTime ToLocal(DateTime utc) => Zoned.ToLocal(utc, Zone);
}
