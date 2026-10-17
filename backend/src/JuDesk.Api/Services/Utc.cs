namespace JuDesk.Api.Services;

public static class Utc
{
    /// <summary>
    /// Npgsql only writes <c>timestamptz</c> from UTC values. JSON dates arrive as UTC ("Z"),
    /// with an offset (parsed as Local) or without any zone (Unspecified, treated as UTC here).
    /// </summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
