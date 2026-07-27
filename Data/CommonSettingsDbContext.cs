using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class CommonSettingsDbContext(DbContextOptions<CommonSettingsDbContext> options) : DbContext(options)
{
    public DbSet<EmployeeColumnSetting> EmployeeColumnSettings => Set<EmployeeColumnSetting>();

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
    }
}
