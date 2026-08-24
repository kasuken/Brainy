using Brainy.Application.Interfaces.Persistence;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Brainy.Data;

/// <summary>
/// The Entity Framework Core context for Brainy. Includes ASP.NET Core Identity
/// schema (via <see cref="IdentityDbContext{TUser}"/>). Entity shapes are configured
/// via <see cref="IEntityTypeConfiguration{TEntity}"/> implementations in the
/// Configurations folder.
/// </summary>
public class BrainyDbContext(
    DbContextOptions<BrainyDbContext> options,
    TimeProvider? timeProvider = null)
    : IdentityDbContext<ApplicationUser>(options), IApplicationDbContext
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteImage> NoteImages => Set<NoteImage>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Highlight> Highlights => Set<Highlight>();

    public DbSet<Summary> Summaries => Set<Summary>();

    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    public DbSet<NoteRelationship> NoteRelationships => Set<NoteRelationship>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    public DbSet<WeeklyTaskSelection> WeeklyTaskSelections => Set<WeeklyTaskSelection>();

    public DbSet<Output> Outputs => Set<Output>();

    public DbSet<ArchiveRetentionRule> ArchiveRetentionRules => Set<ArchiveRetentionRule>();

    public DbSet<UserDashboardPreference> DashboardPreferences => Set<UserDashboardPreference>();

    public DbSet<Idea> Ideas => Set<Idea>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();

    public DbSet<GoalActivity> GoalActivities => Set<GoalActivity>();

    public DbSet<LifecycleActivity> LifecycleActivities => Set<LifecycleActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Identity schema first, then apply Brainy entity configurations.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BrainyDbContext).Assembly);

        // Optimistic concurrency: every BaseEntity-derived table gets a rowversion
        // token so concurrent edits (multiple tabs/circuits) fail with
        // DbUpdateConcurrencyException instead of silently overwriting each other.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion();
            }
        }
    }

    public override int SaveChanges()
    {
        EnsureLifecycleActivitiesAreAppendOnly();
        AppendLifecycleActivities();
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureLifecycleActivitiesAreAppendOnly();
        AppendLifecycleActivities();
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteSerializedTaskDependencyMutationAsync<TResult>(
        string userId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(operation);

        // EF InMemory does not support transactions. Its tests are single-process and
        // exercise business validation, so execute directly instead of raising the
        // provider's TransactionIgnoredWarning as an exception.
        if (!Database.IsRelational())
            return await operation(cancellationToken).ConfigureAwait(false);

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async strategyCancellationToken =>
        {
            // A retry must reload the graph from the database rather than reuse state
            // left in the tracker by a failed transaction attempt.
            ChangeTracker.Clear();

            await using var transaction = await Database
                .BeginTransactionAsync(IsolationLevel.Serializable, strategyCancellationToken)
                .ConfigureAwait(false);

            if (Database.IsSqlServer())
            {
                await AcquireTaskDependencyGraphLockAsync(userId, strategyCancellationToken)
                    .ConfigureAwait(false);
            }

            var result = await operation(strategyCancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(strategyCancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task AcquireTaskDependencyGraphLockAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var userIdHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
        var resource = $"Brainy:TaskDependencies:{userIdHash}";

        await Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = -1;
            IF @result < 0
                THROW 51000, 'Unable to serialize task dependency update.', 1;
            """, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureLifecycleActivitiesAreAppendOnly()
    {
        if (ChangeTracker.Entries<LifecycleActivity>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Lifecycle activity is append-only and cannot be modified or deleted.");
        }
    }

    private void ApplyAuditTimestamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

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

    private void AppendLifecycleActivities()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var activities = new List<LifecycleActivity>();

        foreach (var entry in ChangeTracker.Entries<Note>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var note = entry.Entity;
            if (entry.State == EntityState.Added)
                Add(note.UserId, note.Id, Domain.Enums.PulseActivityType.NoteCaptured, note.CreatedAtUtc, note.Title, "Captured", $"/notes/{note.Id}");

            if ((entry.State == EntityState.Added && note.ProcessedAtUtc.HasValue) ||
                (entry.State == EntityState.Modified && entry.Property(n => n.ProcessedAtUtc).OriginalValue is null && note.ProcessedAtUtc.HasValue))
                Add(note.UserId, note.Id, Domain.Enums.PulseActivityType.NoteProcessed, note.ProcessedAtUtc, note.Title, "Processed from Inbox", $"/notes/{note.Id}");

            AppendArchiveTransition(entry, note.UserId, note.Id, note.Title, note.IsArchived,
                note.ArchivedAtUtc, Domain.Enums.PulseActivityType.NoteArchived,
                Domain.Enums.PulseActivityType.NoteRestored, $"/notes/{note.Id}");
        }

        foreach (var entry in ChangeTracker.Entries<TaskItem>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var task = entry.Entity;
            if (entry.State == EntityState.Added)
                Add(task.UserId, task.Id, Domain.Enums.PulseActivityType.TaskCreated, task.CreatedAtUtc, task.Title, "Created", $"/projects/{task.ProjectId}");

            var originalStatus = entry.State == EntityState.Modified
                ? entry.Property(t => t.Status).OriginalValue
                : default;
            if (task.Status == Domain.Enums.TaskItemStatus.Done &&
                (entry.State == EntityState.Added || originalStatus != Domain.Enums.TaskItemStatus.Done))
                Add(task.UserId, task.Id, Domain.Enums.PulseActivityType.TaskCompleted,
                    task.CompletedDate, task.Title, "Completed", $"/projects/{task.ProjectId}");
            else if (entry.State == EntityState.Modified && originalStatus == Domain.Enums.TaskItemStatus.Done &&
                     task.Status != Domain.Enums.TaskItemStatus.Done)
                Add(task.UserId, task.Id, Domain.Enums.PulseActivityType.TaskReopened,
                    now, task.Title, "Reopened", $"/projects/{task.ProjectId}");

            AppendArchiveTransition(entry, task.UserId, task.Id, task.Title, task.IsArchived,
                task.ArchivedAtUtc, Domain.Enums.PulseActivityType.TaskArchived,
                Domain.Enums.PulseActivityType.TaskRestored, $"/projects/{task.ProjectId}");
        }

        foreach (var entry in ChangeTracker.Entries<Project>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var project = entry.Entity;
            if (entry.State == EntityState.Added)
                Add(project.UserId, project.Id, Domain.Enums.PulseActivityType.ProjectCreated,
                    project.CreatedAtUtc, project.Name, "Created", $"/projects/{project.Id}");

            var originalStatus = entry.State == EntityState.Modified
                ? entry.Property(p => p.Status).OriginalValue
                : default;
            var isRestore = entry.State == EntityState.Modified &&
                            entry.Property(p => p.IsArchived).OriginalValue && !project.IsArchived;
            if (project.Status == Domain.Enums.ProjectStatus.Completed &&
                !isRestore &&
                (entry.State == EntityState.Added || originalStatus != Domain.Enums.ProjectStatus.Completed))
                Add(project.UserId, project.Id, Domain.Enums.PulseActivityType.ProjectCompleted,
                    project.CompletedDate, project.Name, "Completed", $"/projects/{project.Id}");

            AppendArchiveTransition(entry, project.UserId, project.Id, project.Name, project.IsArchived,
                project.ArchivedAtUtc, Domain.Enums.PulseActivityType.ProjectArchived,
                Domain.Enums.PulseActivityType.ProjectRestored, $"/projects/{project.Id}");
        }

        foreach (var entry in ChangeTracker.Entries<Output>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var output = entry.Entity;
            if (entry.State == EntityState.Added)
                Add(output.UserId, output.Id, Domain.Enums.PulseActivityType.OutputCreated,
                    output.CreatedAtUtc, output.Title, "Created", $"/outputs/{output.Id}");

            var originalStatus = entry.State == EntityState.Modified
                ? entry.Property(o => o.Status).OriginalValue
                : default;
            if (output.Status == Domain.Enums.OutputStatus.Published &&
                (entry.State == EntityState.Added || originalStatus != Domain.Enums.OutputStatus.Published))
                Add(output.UserId, output.Id, Domain.Enums.PulseActivityType.OutputPublished,
                    output.PublishedDate, output.Title, "Published", $"/outputs/{output.Id}");

            AppendArchiveTransition(entry, output.UserId, output.Id, output.Title, output.IsArchived,
                output.ArchivedDate, Domain.Enums.PulseActivityType.OutputArchived,
                Domain.Enums.PulseActivityType.OutputRestored, $"/outputs/{output.Id}");
        }

        foreach (var entry in ChangeTracker.Entries<Idea>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var idea = entry.Entity;
            if (entry.State == EntityState.Added)
                Add(idea.UserId, idea.Id, Domain.Enums.PulseActivityType.IdeaCaptured,
                    idea.CreatedAtUtc, idea.Title, "Captured", $"/ideas/{idea.Id}");

            var originalStatus = entry.State == EntityState.Modified
                ? entry.Property(i => i.Status).OriginalValue
                : default;
            if (idea.Status == Domain.Enums.IdeaStatus.Committed &&
                (entry.State == EntityState.Added || originalStatus != Domain.Enums.IdeaStatus.Committed))
                Add(idea.UserId, idea.Id, Domain.Enums.PulseActivityType.IdeaCommitted,
                    idea.CommittedAtUtc, idea.Title, "Committed", $"/ideas/{idea.Id}");
        }

        foreach (var entry in ChangeTracker.Entries<Goal>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var goal = entry.Entity;
            var originalStatus = entry.State == EntityState.Modified
                ? entry.Property(g => g.Status).OriginalValue
                : default;
            if (goal.Status == Domain.Enums.GoalStatus.Achieved &&
                (entry.State == EntityState.Added || originalStatus != Domain.Enums.GoalStatus.Achieved))
                Add(goal.UserId, goal.Id, Domain.Enums.PulseActivityType.GoalAchieved,
                    goal.AchievedDate, goal.Title, "Achieved", $"/goals/{goal.Id}");
        }

        if (activities.Count > 0)
            LifecycleActivities.AddRange(activities);

        return;

        void Add(string userId, Guid entityId, Domain.Enums.PulseActivityType type,
            DateTime? occurredAtUtc, string title, string? context, string? link)
        {
            // A failed SaveChanges leaves Added entries tracked. Do not append a second
            // copy if the caller corrects the failure and retries on the same context.
            if (ChangeTracker.Entries<LifecycleActivity>().Any(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.UserId == userId &&
                    entry.Entity.EntityId == entityId &&
                    entry.Entity.ActivityType == type))
            {
                return;
            }

            activities.Add(new LifecycleActivity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EntityId = entityId,
                ActivityType = type,
                OccurredAtUtc = occurredAtUtc is { } occurred && occurred != default ? occurred : now,
                Title = title,
                Context = context,
                Link = link,
            });
        }

        void AppendArchiveTransition<TEntity>(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
            string userId,
            Guid entityId,
            string title,
            bool isArchived,
            DateTime? archivedAtUtc,
            Domain.Enums.PulseActivityType archivedType,
            Domain.Enums.PulseActivityType restoredType,
            string link) where TEntity : class
        {
            var originalArchived = entry.State == EntityState.Modified
                ? (bool)entry.Property(nameof(TaskItem.IsArchived)).OriginalValue!
                : false;

            if (isArchived && (entry.State == EntityState.Added || !originalArchived))
                Add(userId, entityId, archivedType, archivedAtUtc, title, "Archived", link);
            else if (entry.State == EntityState.Modified && originalArchived && !isArchived)
                Add(userId, entityId, restoredType, now, title, "Restored", link);
        }
    }
}
