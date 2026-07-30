using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class CommonSettingsDbContext(DbContextOptions<CommonSettingsDbContext> options) : DbContext(options)
{
    public DbSet<EmployeeColumnSetting> EmployeeColumnSettings => Set<EmployeeColumnSetting>();
    public DbSet<SalaryPositionAxisSetting> SalaryPositionAxisSettings => Set<SalaryPositionAxisSetting>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<EmployeeDatabaseChange> EmployeeDatabaseChanges => Set<EmployeeDatabaseChange>();
    public DbSet<HireEmployee> HireEmployees => Set<HireEmployee>();
    public DbSet<TerminationEmployee> TerminationEmployees => Set<TerminationEmployee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeColumnSetting>(entity =>
        {
            entity.ToTable("EmployeeColumnSettings");
            entity.HasKey(x=>x.ColumnKey);
            entity.Property(x=>x.ColumnKey).HasMaxLength(50);
            entity.Property(x=>x.DisplayName).HasMaxLength(50).IsRequired();
            entity.HasIndex(x=>x.DisplayName).IsUnique();
        });
        modelBuilder.Entity<SalaryPositionAxisSetting>(entity =>
        {
            entity.ToTable("SalaryPositionAxisSettings");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.PositionName).HasMaxLength(50).IsRequired();
            entity.HasIndex(x=>x.PositionName).IsUnique();
        });
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.LoginId).HasMaxLength(120).IsRequired();
            entity.Property(x=>x.PasswordHash).HasMaxLength(300).IsRequired();
            entity.Property(x=>x.Role).HasMaxLength(30).IsRequired();
            entity.Property(x=>x.Theme).HasMaxLength(10).IsRequired();
            entity.HasIndex(x=>x.LoginId).IsUnique();
        });
        modelBuilder.Entity<EmployeeDatabaseChange>(entity =>
        {
            entity.ToTable("EmployeeDatabaseChanges");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.UserName).HasMaxLength(120).IsRequired();
            entity.Property(x=>x.Action).HasMaxLength(50).IsRequired();
            entity.Property(x=>x.Detail).HasMaxLength(300).IsRequired();
            entity.HasIndex(x=>x.OccurredAtUtc);
        });
        modelBuilder.Entity<HireEmployee>(entity =>
        {
            entity.ToTable("HireEmployees");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.EmployeeNumber).HasMaxLength(50).IsRequired();
            entity.Property(x=>x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x=>x.Department).HasMaxLength(100);
            entity.Property(x=>x.Position).HasMaxLength(50);
            entity.Property(x=>x.Source).HasMaxLength(30).IsRequired();
            entity.Property(x=>x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x=>x.CreatedBy).HasMaxLength(120).IsRequired();
            entity.HasIndex(x=>new{x.EmployeeNumber,x.HireDate}).IsUnique();
            entity.HasIndex(x=>x.HireDate);
        });
        modelBuilder.Entity<TerminationEmployee>(entity =>
        {
            entity.ToTable("TerminationEmployees");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.EmployeeNumber).HasMaxLength(50).IsRequired();
            entity.Property(x=>x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x=>x.Department).HasMaxLength(100);
            entity.Property(x=>x.Position).HasMaxLength(50);
            entity.HasIndex(x=>new{x.EmployeeNumber,x.TerminationDate}).IsUnique();
            entity.HasIndex(x=>x.TerminationDate);
        });
    }
}
