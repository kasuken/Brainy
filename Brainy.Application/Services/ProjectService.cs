using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Common;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Project"/> entities, scoped to the current user.
/// Active projects exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class ProjectService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache) : IProjectService
{
    public async Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "projects:active",
            ProjectReadTags(),
            async ct =>
            {
                var projects = await ProjectQuery(userId)
                    .Where(p => p.Status == ProjectStatus.Active)
                    .OrderByDescending(p => p.Priority)
                    .ThenBy(p => p.Name)
                    .ToListAsync(ct).ConfigureAwait(false);
                return projects.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "projects:non-archived",
            ProjectReadTags(),
            async ct =>
            {
                var projects = await ProjectQuery(userId)
                    .Where(p => p.Status != ProjectStatus.Archived && !p.IsArchived)
                    .OrderByDescending(p => p.Priority)
                    .ThenBy(p => p.Status)
                    .ThenBy(p => p.Name)
                    .ToListAsync(ct).ConfigureAwait(false);
                return projects.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "projects:archived",
            ProjectReadTags(),
            async ct =>
            {
                var projects = await ProjectQuery(userId)
                    .Where(p => p.IsArchived || p.Status == ProjectStatus.Archived)
                    .OrderByDescending(p => p.ArchivedAtUtc)
                    .ThenBy(p => p.Name)
                    .ToListAsync(ct).ConfigureAwait(false);
                return projects.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetProjectSummariesAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:summaries:{today:yyyy-MM-dd}",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            ct => GetProjectSummariesCoreAsync(userId, today, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ProjectSummaryDto>> GetProjectSummariesCoreAsync(
        string userId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var data = await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsArchived && p.Status != ProjectStatus.Archived)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.DesiredOutcome, p.Status, p.Priority,
                p.StartDate, p.DueDate, p.CompletedDate, p.IsArchived, p.AreaId,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.ArchivedAtUtc, p.ArchivedReason,
                p.Emoji,
                TotalTasks   = p.Tasks.Count(t => !t.IsArchived),
                OpenTasks    = p.Tasks.Count(t => !t.IsArchived && t.Status != TaskItemStatus.Done),
                DoneTasks    = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.Done),
                OverdueTasks = p.Tasks.Count(t => !t.IsArchived
                                                  && t.Status != TaskItemStatus.Done
                                                  && t.DueDate.HasValue
                                                  && t.DueDate.Value.Date < today),
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return data.Select(x => new ProjectSummaryDto(
            x.Id, x.Name, x.Description, x.DesiredOutcome, x.Status, x.Priority,
            x.StartDate, x.DueDate, x.CompletedDate, x.IsArchived, x.AreaId,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.ArchivedAtUtc,
            x.TotalTasks, x.OpenTasks, x.DoneTasks,
            x.TotalTasks > 0 ? Math.Round((double)x.DoneTasks / x.TotalTasks * 100, 1) : 0,
            x.OverdueTasks,
            NormalizeEmoji(x.Emoji),
            x.ArchivedReason))
            .ToList();
    }

    public async Task<ProjectDetailDto?> GetProjectDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:{id}:detail",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTag<Project>(id),
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Note>()
            ],
            ct => GetProjectDetailCoreAsync(id, userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectDetailDto?> GetProjectDetailCoreAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Include(p => p.Goal)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (project is null) return null;

        // Top-level tasks only (subtasks are loaded separately and nested). Archived
        // project context deliberately includes its archived tasks for reference.
        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == id && t.UserId == userId
                        && (!t.IsArchived || project.IsArchived)
                        && t.ParentTaskId == null)
            .OrderBy(t => t.Status)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Select(t => new
            {
                t.Id, t.Title, t.Description, t.Status, t.Priority,
                t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
                t.CreatedAtUtc, t.UpdatedAtUtc,
                SubtaskCount     = t.Subtasks.Count(s => !s.IsArchived),
                DoneSubtaskCount = t.Subtasks.Count(s => !s.IsArchived && s.Status == TaskItemStatus.Done),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Load subtasks for all top-level tasks in one query, then group by parent
        var topLevelIds = tasks.Select(t => t.Id).ToList();
        var subtasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == id && t.UserId == userId
                        && (!t.IsArchived || project.IsArchived)
                        && t.ParentTaskId != null && topLevelIds.Contains(t.ParentTaskId.Value))
            .OrderBy(t => t.Status)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Select(t => new
            {
                t.Id, t.Title, t.Description, t.Status, t.Priority,
                t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
                t.CreatedAtUtc, t.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subtasksByParent = subtasks
            .GroupBy(s => s.ParentTaskId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskItemDto>)g.Select(s => new TaskItemDto(
                    s.Id, s.Title, s.Description, s.Status, s.Priority,
                    s.DueDate, s.CompletedDate, s.IsArchived, s.IsCurrentTask, s.ProjectId, s.ParentTaskId,
                    s.CreatedAtUtc, s.UpdatedAtUtc, 0, 0)).ToList());

        var notes = await context.Notes
            .AsNoTracking()
            .Where(n => n.ProjectId == id && n.UserId == userId && n.Status != NoteStatus.Archived)
            .Where(n => n.ParaCategory != ParaCategory.Resource)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new NoteDto(n.Id, n.Title, n.Content, n.AiSummary,
                n.Status, n.IsArchived, n.ArchivedAtUtc, n.ProcessedAtUtc, n.ParaCategory, n.SourceId, n.ProjectId, n.AreaId, n.ResourceId,
                n.CreatedAtUtc, n.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var resourceNotes = await context.Notes
            .AsNoTracking()
            .Where(n => n.ProjectId == id && n.UserId == userId && n.Status != NoteStatus.Archived)
            .Where(n => n.ParaCategory == ParaCategory.Resource)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new NoteDto(n.Id, n.Title, n.Content, n.AiSummary,
                n.Status, n.IsArchived, n.ArchivedAtUtc, n.ProcessedAtUtc, n.ParaCategory, n.SourceId, n.ProjectId, n.AreaId, n.ResourceId,
                n.CreatedAtUtc, n.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskDtos = tasks.Select(t => new TaskItemDto(
            t.Id, t.Title, t.Description, t.Status, t.Priority,
            t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
            t.CreatedAtUtc, t.UpdatedAtUtc, t.SubtaskCount, t.DoneSubtaskCount,
            subtasksByParent.GetValueOrDefault(t.Id)))
            .ToList();

        int totalTasks = await context.Tasks.CountAsync(
            t => t.ProjectId == id && t.UserId == userId && !t.IsArchived, cancellationToken)
            .ConfigureAwait(false);
        int doneTasks = await context.Tasks.CountAsync(
            t => t.ProjectId == id && t.UserId == userId && !t.IsArchived && t.Status == TaskItemStatus.Done, cancellationToken)
            .ConfigureAwait(false);
        int openTasks = totalTasks - doneTasks;

        return new ProjectDetailDto(
            project.Id, project.Name, project.Description, project.DesiredOutcome,
            project.Status, project.Priority, project.StartDate, project.DueDate,
            project.CompletedDate, project.IsArchived, project.AreaId, project.GoalId, project.Goal?.Title,
            project.CreatedAtUtc, project.UpdatedAtUtc, project.ArchivedAtUtc,
            totalTasks, openTasks, doneTasks,
            totalTasks > 0 ? Math.Round((double)doneTasks / totalTasks * 100, 1) : 0,
            taskDtos, notes, resourceNotes,
            NormalizeEmoji(project.Emoji), project.ArchivedReason);
    }

    public async Task<ProjectProgressDto?> GetProjectProgressAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:{id}:progress",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTag<Project>(id),
                ApplicationCacheKey.EntityTypeTag<TaskItem>()
            ],
            ct => GetProjectProgressCoreAsync(id, userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectProgressDto?> GetProjectProgressCoreAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken)
    {
        // Verify ownership without loading the full project
        var exists = await context.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists) return null;

        var total = await context.Tasks
            .CountAsync(t => t.ProjectId == id && t.UserId == userId && !t.IsArchived, cancellationToken)
            .ConfigureAwait(false);

        var done = await context.Tasks
            .CountAsync(t => t.ProjectId == id && t.UserId == userId && !t.IsArchived && t.Status == TaskItemStatus.Done, cancellationToken)
            .ConfigureAwait(false);

        return new ProjectProgressDto(
            id,
            total,
            done,
            total - done,
            total > 0 ? Math.Round((double)done / total * 100, 1) : 0,
            DateTime.UtcNow);
    }

    public async Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:{id}:summary",
            ProjectReadTags(id),
            async ct =>
            {
                var project = await ProjectQuery(userId)
                    .FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
                return project is null ? null : ToDto(project);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (dto.GoalId.HasValue)
        {
            var goalExists = await context.Goals
                .AnyAsync(g => g.Id == dto.GoalId.Value && g.UserId == userId && !g.IsArchived && g.Status != GoalStatus.Archived, cancellationToken)
                .ConfigureAwait(false);

            if (!goalExists)
                throw new KeyNotFoundException($"Goal '{dto.GoalId}' was not found.");
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Emoji = NormalizeEmoji(dto.Emoji),
            Description = dto.Description,
            DesiredOutcome = dto.DesiredOutcome,
            Status = dto.Status,
            Priority = dto.Priority,
            StartDate = dto.StartDate,
            DueDate = dto.DueDate,
            AreaId = dto.AreaId,
            GoalId = dto.GoalId
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateProjectAsync(userId, project.Id).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task<ProjectDto> UpdateAsync(UpdateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (dto.GoalId.HasValue)
        {
            var goalExists = await context.Goals
                .AnyAsync(g => g.Id == dto.GoalId.Value && g.UserId == userId && !g.IsArchived && g.Status != GoalStatus.Archived, cancellationToken)
                .ConfigureAwait(false);

            if (!goalExists)
                throw new KeyNotFoundException($"Goal '{dto.GoalId}' was not found.");
        }

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == dto.Id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{dto.Id}' was not found.");

        // Optimistic concurrency: compare against the token captured when the caller
        // loaded the project so edits made elsewhere since then are detected.
        if (dto.RowVersion is not null)
            context.Entry(project).Property(p => p.RowVersion).OriginalValue = dto.RowVersion;

        project.Name = dto.Name;
        project.Emoji = NormalizeEmoji(dto.Emoji);
        project.Description = dto.Description;
        project.DesiredOutcome = dto.DesiredOutcome;
        project.Status = dto.Status;
        project.Priority = dto.Priority;
        project.StartDate = dto.StartDate;
        project.DueDate = dto.DueDate;
        project.AreaId = dto.AreaId;
        project.GoalId = dto.GoalId;

        if (dto.Status == ProjectStatus.Completed && project.CompletedDate is null)
            project.CompletedDate = DateTime.UtcNow;
        else if (dto.Status != ProjectStatus.Completed)
            project.CompletedDate = null;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("project", ex);
        }
        await InvalidateProjectAsync(userId, project.Id).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task<ProjectDto> CompleteAsync(Guid id, TaskCompletionAction taskAction, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        var now = DateTime.UtcNow;

        // Mark the project completed
        project.Status        = ProjectStatus.Completed;
        project.CompletedDate = project.CompletedDate ?? now;

        // Handle remaining open tasks
        var openTasks = project.Tasks
            .Where(t => !t.IsArchived && t.Status != Domain.Enums.TaskItemStatus.Done)
            .ToList();

        switch (taskAction)
        {
            case TaskCompletionAction.CompleteAll:
                foreach (var task in openTasks)
                {
                    task.Status        = Domain.Enums.TaskItemStatus.Done;
                    task.CompletedDate = now;
                    task.IsCurrentTask = false;
                }
                break;

            case TaskCompletionAction.ArchiveAll:
                foreach (var task in openTasks)
                {
                    task.IsArchived    = true;
                    task.ArchivedAtUtc = now;
                    task.IsCurrentTask = false;
                }
                break;

            case TaskCompletionAction.LeaveAsIs:
            default:
                break;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateProjectAsync(
            userId,
            project.Id,
            taskAction == TaskCompletionAction.LeaveAsIs
                ? []
                : openTasks.Select(task => task.Id)).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        if (project.IsArchived)
            return;

        var now = DateTime.UtcNow;
        var archiveOperationId = Guid.NewGuid();
        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        project.StatusBeforeArchive = project.Status;
        project.ArchiveOperationId = archiveOperationId;
        project.IsArchived    = true;
        project.ArchivedAtUtc = now;
        project.ArchivedReason = normalizedReason;
        project.Status        = ProjectStatus.Archived;

        // Cascade: archive all non-archived tasks belonging to the project
        var changedTasks = project.Tasks.Where(t => !t.IsArchived).ToList();
        foreach (var task in changedTasks)
        {
            task.IsArchived    = true;
            task.ArchivedAtUtc = now;
            task.ArchivedReason = normalizedReason;
            task.ArchiveOperationId = archiveOperationId;
            task.IsCurrentTask = false;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateProjectAsync(
            userId,
            project.Id,
            changedTasks.Select(task => task.Id)).ConfigureAwait(false);
    }

    public async Task<ProjectDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        if (!project.IsArchived)
            return ToDto(project);

        await context.Areas.EnsureActiveOwnedAreaAsync(project.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        var archiveOperationId = project.ArchiveOperationId;
        project.IsArchived    = false;
        project.ArchivedAtUtc = null;
        project.ArchivedReason = null;
        project.Status        = project.StatusBeforeArchive ?? ProjectStatus.NotStarted;
        project.StatusBeforeArchive = null;
        project.ArchiveOperationId = null;

        // Restore only tasks archived by this project operation. Tasks archived
        // manually before the project entered Archives remain archived.
        var changedTasks = project.Tasks.Where(t =>
            t.IsArchived && archiveOperationId.HasValue && t.ArchiveOperationId == archiveOperationId).ToList();
        foreach (var task in changedTasks)
        {
            task.IsArchived    = false;
            task.ArchivedAtUtc = null;
            task.ArchivedReason = null;
            task.ArchiveOperationId = null;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateProjectAsync(
            userId,
            project.Id,
            changedTasks.Select(task => task.Id)).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .Include(p => p.Tasks)
            .Include(p => p.Notes)
            .Include(p => p.Outputs)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        if (project.Tasks.Count > 0 || project.Notes.Count > 0 || project.Outputs.Count > 0)
            throw new InvalidOperationException(
                $"Project '{project.Name}' cannot be deleted because it still has " +
                $"{project.Tasks.Count} task(s), {project.Notes.Count} note(s), and " +
                $"{project.Outputs.Count} output(s). Remove or reassign them first.");

        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTag<Project>(project.Id),
                ApplicationCacheKey.EntityTypeTag<Idea>()
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.DesiredOutcome,
        p.Status,
        p.Priority,
        p.StartDate,
        p.DueDate,
        p.CompletedDate,
        p.IsArchived,
        p.AreaId,
        p.CreatedAtUtc,
        p.UpdatedAtUtc,
        p.ArchivedAtUtc,
        p.GoalId,
        p.Goal?.Title,
        NormalizeEmoji(p.Emoji),
        RowVersion: p.RowVersion,
        ArchivedReason: p.ArchivedReason);

    private IQueryable<Project> ProjectQuery(string userId) =>
        context.Projects
            .AsNoTracking()
            .Include(p => p.Goal)
            .Where(p => p.UserId == userId);

    private static IReadOnlyCollection<string> ProjectReadTags(Guid? projectId = null)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Project>(),
            ApplicationCacheKey.EntityTypeTag<Goal>()
        ];
        if (projectId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<Project>(projectId.Value));
        return tags;
    }

    private ValueTask InvalidateProjectAsync(
        string userId,
        Guid projectId,
        IEnumerable<Guid>? taskIds = null)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Project>(),
            ApplicationCacheKey.EntityTag<Project>(projectId),
            ApplicationCacheKey.EntityTypeTag<LifecycleActivity>()
        ];
        if (taskIds is not null)
        {
            var ids = taskIds.ToList();
            if (ids.Count > 0)
            {
                tags.Add(ApplicationCacheKey.EntityTypeTag<TaskItem>());
                tags.AddRange(ids.Select(ApplicationCacheKey.EntityTag<TaskItem>));
            }
        }

        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private static string NormalizeEmoji(string? emoji)
    {
        var normalized = string.IsNullOrWhiteSpace(emoji)
            ? ProjectEmojiDefaults.DefaultEmoji
            : emoji.Trim();

        if (normalized.Length > ProjectEmojiDefaults.MaxLength)
            throw new ArgumentException($"Project emoji cannot exceed {ProjectEmojiDefaults.MaxLength} characters.", nameof(emoji));

        return normalized;
    }

    // ── Deadline monitoring ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetDueTodayProjectsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:due-today:{today:yyyy-MM-dd}",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            async ct =>
            {
                var data = await BuildDeadlineSummaryQuery(userId, today)
                    .Where(p => p.DueDate.HasValue && p.DueDate.Value.Date == today)
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Name)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                return (IReadOnlyList<ProjectSummaryDto>)data.Select(x => MapToSummary(x, today)).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetDueThisWeekProjectsAsync(CancellationToken cancellationToken = default)
    {
        var userId   = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today    = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var tomorrow = today.AddDays(1);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var weekEnd  = today.AddDays(daysUntilSunday);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:due-this-week:{today:yyyy-MM-dd}",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            async ct =>
            {
                var data = await BuildDeadlineSummaryQuery(userId, today)
                    .Where(p => p.DueDate.HasValue
                                && p.DueDate.Value.Date >= tomorrow
                                && p.DueDate.Value.Date <= weekEnd)
                    .OrderBy(x => x.DueDate)
                    .ThenByDescending(x => x.Priority)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                return (IReadOnlyList<ProjectSummaryDto>)data.Select(x => MapToSummary(x, today)).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetOverdueProjectsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"projects:overdue:{today:yyyy-MM-dd}",
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            async ct =>
            {
                var data = await BuildDeadlineSummaryQuery(userId, today)
                    .Where(p => p.DueDate.HasValue && p.DueDate.Value.Date < today)
                    .OrderBy(x => x.DueDate)
                    .ThenByDescending(x => x.Priority)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                return (IReadOnlyList<ProjectSummaryDto>)data.Select(x => MapToSummary(x, today)).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Base query for deadline monitoring: non-archived, non-completed, non-archived projects
    /// with a due date set. Includes task subquery projections.
    /// </summary>
    private IQueryable<DeadlineProjection> BuildDeadlineSummaryQuery(string userId, DateTime today) =>
        context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId
                        && !p.IsArchived
                        && p.Status != ProjectStatus.Archived
                        && p.Status != ProjectStatus.Completed
                        && p.DueDate.HasValue)
            .Select(p => new DeadlineProjection
            {
                Id           = p.Id,
                Name         = p.Name,
                Emoji        = p.Emoji,
                Description  = p.Description,
                DesiredOutcome = p.DesiredOutcome,
                Status       = p.Status,
                Priority     = p.Priority,
                StartDate    = p.StartDate,
                DueDate      = p.DueDate,
                CompletedDate = p.CompletedDate,
                IsArchived   = p.IsArchived,
                AreaId       = p.AreaId,
                CreatedAtUtc = p.CreatedAtUtc,
                UpdatedAtUtc = p.UpdatedAtUtc,
                ArchivedAtUtc = p.ArchivedAtUtc,
                ArchivedReason = p.ArchivedReason,
                TotalTasks   = p.Tasks.Count(t => !t.IsArchived),
                OpenTasks    = p.Tasks.Count(t => !t.IsArchived && t.Status != TaskItemStatus.Done),
                DoneTasks    = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.Done),
                OverdueTasks = p.Tasks.Count(t => !t.IsArchived
                                                  && t.Status != TaskItemStatus.Done
                                                  && t.DueDate.HasValue
                                                  && t.DueDate.Value.Date < today),
            });

    private static ProjectSummaryDto MapToSummary(DeadlineProjection x, DateTime _) => new(
        x.Id, x.Name, x.Description, x.DesiredOutcome, x.Status, x.Priority,
        x.StartDate, x.DueDate, x.CompletedDate, x.IsArchived, x.AreaId,
        x.CreatedAtUtc, x.UpdatedAtUtc, x.ArchivedAtUtc,
        x.TotalTasks, x.OpenTasks, x.DoneTasks,
        x.TotalTasks > 0 ? Math.Round((double)x.DoneTasks / x.TotalTasks * 100, 1) : 0,
        x.OverdueTasks,
        NormalizeEmoji(x.Emoji),
        x.ArchivedReason);

    /// <summary>Anonymous-type-equivalent for EF projection in deadline queries.</summary>
    private sealed class DeadlineProjection
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Emoji { get; init; }
        public string? Description { get; init; }
        public string? DesiredOutcome { get; init; }
        public ProjectStatus Status { get; init; }
        public ProjectPriority Priority { get; init; }
        public DateTime? StartDate { get; init; }
        public DateTime? DueDate { get; init; }
        public DateTime? CompletedDate { get; init; }
        public bool IsArchived { get; init; }
        public Guid? AreaId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public string? ArchivedReason { get; init; }
        public int TotalTasks { get; init; }
        public int OpenTasks { get; init; }
        public int DoneTasks { get; init; }
        public int OverdueTasks { get; init; }
    }
}
