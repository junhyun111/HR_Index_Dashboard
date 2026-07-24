using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class ManagementDbContext(DbContextOptions<ManagementDbContext> options) : DbContext(options)
{
    public DbSet<FinancialReport> FinancialReports => Set<FinancialReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinancialReport>(entity =>
        {
            entity.ToTable("FinancialReports");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.ReportCode).HasMaxLength(10);
            entity.Property(x=>x.ReportName).HasMaxLength(20);
            entity.Property(x=>x.FsDiv).HasMaxLength(10);
            entity.Property(x=>x.EmployeeCountBasis).HasMaxLength(100);
            entity.HasIndex(x=>new{x.BusinessYear,x.ReportCode}).IsUnique();
        });
    }
}
