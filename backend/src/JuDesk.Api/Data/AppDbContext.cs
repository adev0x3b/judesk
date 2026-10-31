using JuDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JuDesk.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<SetupCode> SetupCodes => Set<SetupCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(200);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.DisplayName).HasMaxLength(120);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Skill>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(80);
            e.HasIndex(s => s.Name).IsUnique();
            e.HasMany(s => s.Technicians)
                .WithMany(t => t.Skills)
                .UsingEntity("TechnicianSkills");
        });

        modelBuilder.Entity<Technician>(e =>
        {
            e.Property(t => t.FullName).HasMaxLength(120);
            e.Property(t => t.Email).HasMaxLength(200);
            e.Property(t => t.Phone).HasMaxLength(40);
            e.HasIndex(t => t.FullName);
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(120);
            e.Property(s => s.Address).HasMaxLength(300);
            e.Property(s => s.Color).HasMaxLength(9);
        });

        modelBuilder.Entity<Shift>(e =>
        {
            e.Property(s => s.Notes).HasMaxLength(500);
            e.HasIndex(s => s.StartsAt);
            e.HasOne(s => s.Site).WithMany(site => site.Shifts).HasForeignKey(s => s.SiteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.RequiredSkill).WithMany().HasForeignKey(s => s.RequiredSkillId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_shift_range", "\"EndsAt\" > \"StartsAt\""));
            e.ToTable(t => t.HasCheckConstraint("ck_shift_headcount", "\"Headcount\" >= 1"));
            e.Ignore(s => s.Duration);
            e.Ignore(s => s.Hours);
        });

        modelBuilder.Entity<SetupCode>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.CodeHash).HasMaxLength(64);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasIndex(a => new { a.ShiftId, a.TechnicianId }).IsUnique();
            e.HasIndex(a => a.TechnicianId);
            e.Property(a => a.Source).HasConversion<string>().HasMaxLength(10);
            e.HasOne(a => a.Shift).WithMany(s => s.Assignments).HasForeignKey(a => a.ShiftId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Technician).WithMany(t => t.Assignments).HasForeignKey(a => a.TechnicianId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
