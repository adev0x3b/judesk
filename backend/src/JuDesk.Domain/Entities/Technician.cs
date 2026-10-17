namespace JuDesk.Domain.Entities;

public class Technician
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Maximum hours this technician may be scheduled in one ISO week (Monday to Sunday).</summary>
    public int MaxWeeklyHours { get; set; } = 40;

    public bool Active { get; set; } = true;
    public ICollection<Skill> Skills { get; set; } = new List<Skill>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
