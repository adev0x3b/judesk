namespace JuDesk.Domain.Entities;

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }

    /// <summary>Start of the shift, UTC.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>End of the shift, UTC. Must be after <see cref="StartsAt"/>.</summary>
    public DateTime EndsAt { get; set; }

    public Guid RequiredSkillId { get; set; }
    public Skill? RequiredSkill { get; set; }

    /// <summary>How many technicians this shift needs.</summary>
    public int Headcount { get; set; } = 1;

    public string? Notes { get; set; }
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();

    public TimeSpan Duration => EndsAt - StartsAt;
    public double Hours => Duration.TotalHours;

    public bool Overlaps(Shift other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
}
