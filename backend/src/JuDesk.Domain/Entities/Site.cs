namespace JuDesk.Domain.Entities;

public class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Color { get; set; }
    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
}
