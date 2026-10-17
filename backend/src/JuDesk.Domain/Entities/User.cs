using System.Text.Json.Serialization;

namespace JuDesk.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    Planner = 0,
    Admin = 1,
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Planner;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
