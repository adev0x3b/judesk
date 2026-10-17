using System.Text.Json.Serialization;

namespace JuDesk.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssignmentSource
{
    Manual = 0,
    Auto = 1,
}

public class Assignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public Guid TechnicianId { get; set; }
    public Technician? Technician { get; set; }
    public AssignmentSource Source { get; set; } = AssignmentSource.Manual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
