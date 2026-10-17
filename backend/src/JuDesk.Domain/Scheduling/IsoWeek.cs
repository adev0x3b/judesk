namespace JuDesk.Domain.Scheduling;

/// <summary>A Monday-to-Sunday week, the unit JuDesk plans and counts hours in.</summary>
public readonly record struct IsoWeek(DateOnly Monday)
{
    public DateOnly Sunday => Monday.AddDays(6);

    /// <summary>Inclusive start, UTC midnight of Monday.</summary>
    public DateTime StartUtc => Monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>Exclusive end, UTC midnight of the following Monday.</summary>
    public DateTime EndUtc => Monday.AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static IsoWeek Containing(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
        return new IsoWeek(date.AddDays(-offset));
    }

    public static IsoWeek Containing(DateTime utc) => Containing(DateOnly.FromDateTime(utc));

    public bool Contains(DateTime utc) => utc >= StartUtc && utc < EndUtc;

    public IsoWeek Next() => new(Monday.AddDays(7));
    public IsoWeek Previous() => new(Monday.AddDays(-7));

    public override string ToString() => $"{Monday:yyyy-MM-dd}..{Sunday:yyyy-MM-dd}";
}
