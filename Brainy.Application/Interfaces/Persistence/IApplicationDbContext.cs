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
    DbSet<WeeklyTaskSelection> WeeklyTaskSelections { get; }
    DbSet<Output> Outputs { get; }
    DbSet<ArchiveRetentionRule> ArchiveRetentionRules { get; }
    DbSet<UserDashboardPreference> DashboardPreferences { get; }
    DbSet<Idea> Ideas { get; }
    DbSet<Goal> Goals { get; }
    DbSet<GoalMilestone> GoalMilestones { get; }
    DbSet<GoalActivity> GoalActivities { get; }
    DbSet<LifecycleActivity> LifecycleActivities { get; }

    /// <summary>
    /// Change-tracker entry for <paramref name="entity"/>; used to set the original
    /// <see cref="BaseEntity.RowVersion"/> for optimistic concurrency checks.
    /// </summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    /// Executes a complete task-dependency graph mutation under a per-user serialization
    /// boundary. Relational implementations must include validation and persistence in
    /// the same retryable transaction; non-relational test providers may execute directly.
    /// </summary>
    /// <typeparam name="TResult">The result returned by the mutation.</typeparam>
    /// <param name="userId">The owner of the task-dependency graph being mutated.</param>
    /// <param name="operation">The complete mutation, including reads, validation, and persistence.</param>
    /// <param name="cancellationToken">A token that cancels the mutation.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    Task<TResult> ExecuteSerializedTaskDependencyMutationAsync<TResult>(
        string userId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
