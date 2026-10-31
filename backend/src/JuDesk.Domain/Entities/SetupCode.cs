namespace JuDesk.Domain.Entities;

/// <summary>
/// The one-time code that lets whoever starts the app for the first time create the admin account.
/// At most one row. Only a hash is stored: the clear code exists in the API log of the start that
/// issued it. The row is deleted when setup completes or after too many wrong guesses.
/// </summary>
public class SetupCode
{
    public const int MaxFailedAttempts = 5;
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public required string CodeHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
