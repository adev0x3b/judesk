namespace JuDesk.Domain.Entities;

/// <summary>
/// The one-time code that lets whoever starts the app for the first time create the admin account.
/// There is at most one row; it is deleted when setup completes. It is stored in clear so the API
/// can print it again on every restart until setup is done: it is a bootstrap secret of an empty
/// system, and anyone who can read this table already owns the database.
/// </summary>
public class SetupCode
{
    public int Id { get; set; } = 1;
    public required string Code { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
