using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeDataState> EmployeeDataStates => Set<EmployeeDataState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("Employees");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Workplace).HasMaxLength(100);
            entity.Property(x => x.ParentDepartment).HasMaxLength(100);
            entity.Property(x => x.Department).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Position).HasMaxLength(50);
            entity.Property(x => x.WorkShift).HasMaxLength(50);
            entity.Property(x => x.Duty).HasMaxLength(50);
            entity.Property(x => x.JobGroup).HasMaxLength(50);
            entity.Property(x => x.EmploymentType).HasMaxLength(50);
            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.Property(x => x.Education).HasMaxLength(100);
            entity.Property(x => x.Major).HasMaxLength(100);
            entity.Property(x => x.AnnualSalary);
            entity.HasIndex(x => x.EmployeeNumber).IsUnique();
            entity.HasIndex(x => x.Department);
            entity.HasIndex(x => x.Name);
        });
        modelBuilder.Entity<EmployeeDataState>(entity =>
        {
            entity.ToTable("EmployeeDataState");
            entity.HasKey(x => x.Id);
        });
    }
}
