namespace JuDesk.Domain.Entities;

public class Skill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public ICollection<Technician> Technicians { get; set; } = new List<Technician>();
}
