using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Data;

/// <summary>
/// The Entity Framework Core context for Brainy. Entity shapes are configured via
/// <see cref="IEntityTypeConfiguration{TEntity}"/> implementations in the Configurations folder.
/// </summary>
public class BrainyDbContext(DbContextOptions<BrainyDbContext> options) : DbContext(options)
{
    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Highlight> Highlights => Set<Highlight>();

    public DbSet<Summary> Summaries => Set<Summary>();

    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    public DbSet<NoteRelationship> NoteRelationships => Set<NoteRelationship>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Output> Outputs => Set<Output>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BrainyDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }
}
