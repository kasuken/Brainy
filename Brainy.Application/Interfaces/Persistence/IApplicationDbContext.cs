using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Brainy.Application.Interfaces.Persistence;

/// <summary>
/// Abstraction over <c>BrainyDbContext</c> exposed to the Application layer.
/// Keeps the Application project free of EF Core infrastructure concerns while
/// still allowing LINQ queries via <see cref="DbSet{TEntity}"/>.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Area> Areas { get; }
    DbSet<Project> Projects { get; }
    DbSet<Resource> Resources { get; }
    DbSet<Source> Sources { get; }
    DbSet<Note> Notes { get; }
    DbSet<NoteImage> NoteImages { get; }
    DbSet<Tag> Tags { get; }
    DbSet<Highlight> Highlights { get; }
    DbSet<Summary> Summaries { get; }
    DbSet<ActionItem> ActionItems { get; }
    DbSet<NoteRelationship> NoteRelationships { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<Output> Outputs { get; }
    DbSet<ArchiveRetentionRule> ArchiveRetentionRules { get; }
    DbSet<UserDashboardPreference> DashboardPreferences { get; }
    DbSet<Idea> Ideas { get; }
    DbSet<Goal> Goals { get; }
    DbSet<GoalMilestone> GoalMilestones { get; }
    DbSet<GoalActivity> GoalActivities { get; }

    /// <summary>
    /// Change-tracker entry for <paramref name="entity"/>; used to set the original
    /// <see cref="BaseEntity.RowVersion"/> for optimistic concurrency checks.
    /// </summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
