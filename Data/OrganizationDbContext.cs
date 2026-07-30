using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Data;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationNode> OrganizationNodes => Set<OrganizationNode>();
    public DbSet<OrganizationState> OrganizationStates => Set<OrganizationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationNode>(entity =>
        {
            entity.ToTable("OrganizationNodes");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.Id).HasMaxLength(100);
            entity.Property(x=>x.Name).HasMaxLength(40).IsRequired();
            entity.Property(x=>x.Type).HasMaxLength(20).IsRequired();
            entity.Property(x=>x.ParentId).HasMaxLength(100);
            entity.HasIndex(x=>x.ParentId);
            entity.HasIndex(x=>new{x.ParentId,x.DisplayOrder});
        });
        modelBuilder.Entity<OrganizationState>(entity =>
        {
            entity.ToTable("OrganizationState");
            entity.HasKey(x=>x.Id);
            entity.Property(x=>x.UpdatedBy).HasMaxLength(120).IsRequired();
        });
    }
}
