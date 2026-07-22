using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(x => x.CompanyName).HasMaxLength(100);
            entity.Property(x => x.DepartmentName).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(50);
            entity.Property(x => x.Grade).HasMaxLength(30);
            entity.Property(x => x.Position).HasMaxLength(50);
            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.HasIndex(x => x.DepartmentName);
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.Grade);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.Property(x => x.UserName).HasMaxLength(256);
            entity.Property(x => x.Action).HasMaxLength(30);
            entity.Property(x => x.Path).HasMaxLength(500);
            entity.HasIndex(x => x.OccurredAtUtc);
        });
    }
}
