namespace JuDesk.Domain.Entities;

/// <summary>
/// One signed-in browser. The auth cookie carries a random session token; only its SHA-256 hash is
/// stored here. Signing out deletes the row, so a copy of the cookie stops working at once; a password
/// change or reset deletes the user's other rows, which signs out every other device.
/// </summary>
public class UserSession
{
    /// <summary>SHA-256 (hex) of the token in the cookie.</summary>
    public required string Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
