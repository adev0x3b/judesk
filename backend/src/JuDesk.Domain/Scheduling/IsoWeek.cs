namespace JuDesk.Domain.Scheduling;

/// <summary>
/// A Monday-to-Sunday week in the installation's time zone, the unit JuDesk plans and counts hours in.
/// Shifts are stored as UTC instants; the week's bounds are the zone's Monday midnights converted to UTC,
/// so a week across a daylight-saving change is 167 or 169 hours long, as it really is.
/// </summary>
public readonly record struct IsoWeek
{
    private readonly TimeZoneInfo? _zone;

    public IsoWeek(DateOnly monday, TimeZoneInfo? zone = null)
    {
        Monday = monday;
        _zone = zone;
    }

    public DateOnly Monday { get; }

    /// <summary>The zone the week is cut in; UTC when none was given.</summary>
    public TimeZoneInfo Zone => _zone ?? TimeZoneInfo.Utc;

    public DateOnly Sunday => Monday.AddDays(6);

    /// <summary>Inclusive start: Monday 00:00 in <see cref="Zone"/>, as UTC.</summary>
    public DateTime StartUtc => Zoned.StartOfDayUtc(Monday, Zone);

    /// <summary>Exclusive end: the following Monday 00:00 in <see cref="Zone"/>, as UTC.</summary>
    public DateTime EndUtc => Zoned.StartOfDayUtc(Monday.AddDays(7), Zone);

    public static IsoWeek Containing(DateOnly date, TimeZoneInfo? zone = null)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
        return new IsoWeek(date.AddDays(-offset), zone);
    }

    /// <summary>The week an instant falls in, judged by its local date in <paramref name="zone"/>.</summary>
    public static IsoWeek Containing(DateTime utc, TimeZoneInfo? zone = null) =>
        Containing(Zoned.LocalDate(utc, zone ?? TimeZoneInfo.Utc), zone);

    public bool Contains(DateTime utc) => utc >= StartUtc && utc < EndUtc;

    public IsoWeek Next() => new(Monday.AddDays(7), _zone);
    public IsoWeek Previous() => new(Monday.AddDays(-7), _zone);

    public bool Equals(IsoWeek other) => Monday == other.Monday && Zone.Id == other.Zone.Id;
    public override int GetHashCode() => HashCode.Combine(Monday, Zone.Id);

    public override string ToString() => $"{Monday:yyyy-MM-dd}..{Sunday:yyyy-MM-dd} ({Zone.Id})";
}

/// <summary>Conversions between UTC instants and the wall clock of a time zone.</summary>
public static class Zoned
{
    /// <summary>The local wall-clock time of a UTC instant.</summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    /// <summary>
    /// The UTC instant of a local wall-clock time. A time skipped by a daylight-saving jump (02:30 on the
    /// spring-forward night in Europe) moves forward to the first valid minute; an ambiguous one (the hour
    /// that repeats in autumn) is taken as the first of the two, the daylight-saving one.
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wall))
        {
            wall = wall.AddMinutes(1);
        }
        if (zone.IsAmbiguousTime(wall))
        {
            var offset = zone.GetAmbiguousTimeOffsets(wall).Max();
            return DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    /// <summary>Local midnight of a date, as UTC (or the first valid minute when midnight is skipped).</summary>
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo zone) => ToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
}
