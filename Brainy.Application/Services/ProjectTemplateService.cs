using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.DTOs.Templates;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Services.Templates;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD for <see cref="ProjectTemplate"/> blueprints and instantiates them into
/// real projects. Instantiation composes <see cref="IProjectService"/> and
/// <see cref="ITaskService"/> rather than writing entities directly, so the normal
/// active-project entitlement check, area/goal ownership validation, and cache
/// invalidation all apply exactly as they would for a hand-made project.
/// </summary>
internal sealed class ProjectTemplateService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache,
    IProjectService projectService,
    ITaskService taskService) : IProjectTemplateService
{
    public async Task<IReadOnlyList<ProjectTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await SeedBuiltInIfEmptyAsync(userId, cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "project-templates:all",
            ReadTags(),
            async ct =>
            {
                var templates = await Query(userId)
                    .OrderBy(t => t.Name)
                    .ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlyList<ProjectTemplateDto>)templates.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"project-templates:{id}",
            ReadTags(id),
            async ct =>
            {
                var template = await Query(userId).FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
                return template is null ? null : ToDto(template);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectTemplateDto> CreateAsync(CreateProjectTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.ProjectNamePattern))
            throw new ArgumentException("Project name pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureOwnedAsync(dto.DefaultAreaId, userId, "Area", cancellationToken).ConfigureAwait(false);
        await context.Goals.EnsureOwnedAsync(dto.DefaultGoalId, userId, "Goal", cancellationToken).ConfigureAwait(false);

        var template = new ProjectTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Trim(),
            ProjectNamePattern = dto.ProjectNamePattern.Trim(),
            Description = dto.Description,
            DesiredOutcome = dto.DesiredOutcome,
            DefaultPriority = dto.DefaultPriority,
            DefaultAreaId = dto.DefaultAreaId,
            DefaultGoalId = dto.DefaultGoalId,
            IsBuiltIn = false,
            Tasks = BuildTaskEntities(dto.Tasks)
        };

        context.ProjectTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<ProjectTemplateDto> UpdateAsync(UpdateProjectTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.ProjectNamePattern))
            throw new ArgumentException("Project name pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureOwnedAsync(dto.DefaultAreaId, userId, "Area", cancellationToken).ConfigureAwait(false);
        await context.Goals.EnsureOwnedAsync(dto.DefaultGoalId, userId, "Goal", cancellationToken).ConfigureAwait(false);

        var template = await context.ProjectTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == dto.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project template '{dto.Id}' was not found.");

        if (dto.RowVersion is not null)
            context.Entry(template).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        template.Name = dto.Name.Trim();
        template.ProjectNamePattern = dto.ProjectNamePattern.Trim();
        template.Description = dto.Description;
        template.DesiredOutcome = dto.DesiredOutcome;
        template.DefaultPriority = dto.DefaultPriority;
        template.DefaultAreaId = dto.DefaultAreaId;
        template.DefaultGoalId = dto.DefaultGoalId;

        // The task list is replaced wholesale: remove the old entries and add the
        // caller's complete desired set rather than diffing individual rows.
        context.ProjectTemplateTasks.RemoveRange(template.Tasks);
        template.Tasks = BuildTaskEntities(dto.Tasks);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("project template", ex);
        }

        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.ProjectTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project template '{id}' was not found.");

        context.ProjectTemplates.Remove(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
    }

    public async Task<ProjectTemplateDto> CreateFromProjectAsync(SaveProjectAsTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.TemplateName))
            throw new ArgumentException("Template name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dto.ProjectId && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{dto.ProjectId}' was not found.");

        // Only top-level, non-archived tasks: a template's default task list is flat
        // (see ProjectTemplateTask), so subtasks are deliberately not captured.
        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == dto.ProjectId && t.UserId == userId && !t.IsArchived && t.ParentTaskId == null)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var template = new ProjectTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.TemplateName.Trim(),
            ProjectNamePattern = project.Name,
            Description = project.Description,
            DesiredOutcome = project.DesiredOutcome,
            DefaultPriority = project.Priority,
            DefaultAreaId = project.AreaId,
            DefaultGoalId = project.GoalId,
            IsBuiltIn = false,
            Tasks = tasks.Select((t, index) => new ProjectTemplateTask
            {
                Id = Guid.NewGuid(),
                Title = t.Title,
                Description = t.Description,
                Priority = t.Priority,
                Complexity = t.Complexity,
                DueDateOffsetDays = t.DueDate.HasValue ? (t.DueDate.Value.Date - today.Date).Days : null,
                SortOrder = index
            }).ToList()
        };

        context.ProjectTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<ProjectDto> InstantiateAsync(InstantiateProjectTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.ProjectTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == dto.TemplateId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project template '{dto.TemplateId}' was not found.");

        var areaId = dto.AreaId ?? template.DefaultAreaId
            ?? throw new ArgumentException(
                "This template has no default area. Choose an area to create the project in.", nameof(dto));

        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? TemplatePatternResolver.Resolve(template.ProjectNamePattern, today)
            : dto.Name.Trim();

        // A plain create through the normal service: the active-project entitlement
        // check, area/goal ownership validation and cache invalidation it already
        // performs all apply here exactly as they would for a hand-made project.
        var project = await projectService.CreateAsync(new CreateProjectDto(
            Name: name,
            AreaId: areaId,
            Description: template.Description,
            DesiredOutcome: template.DesiredOutcome,
            Status: ProjectStatus.NotStarted,
            Priority: template.DefaultPriority,
            StartDate: dto.StartDate,
            DueDate: dto.DueDate,
            GoalId: dto.GoalId ?? template.DefaultGoalId), cancellationToken).ConfigureAwait(false);

        foreach (var templateTask in template.Tasks.OrderBy(t => t.SortOrder))
        {
            var dueDate = templateTask.DueDateOffsetDays.HasValue
                ? today.Date.AddDays(templateTask.DueDateOffsetDays.Value)
                : (DateTime?)null;

            await taskService.CreateAsync(new CreateTaskDto(
                ProjectId: project.Id,
                Title: templateTask.Title,
                Description: templateTask.Description,
                Priority: templateTask.Priority,
                DueDate: dueDate,
                Complexity: templateTask.Complexity), cancellationToken).ConfigureAwait(false);
        }

        return project;
    }

    private static List<ProjectTemplateTask> BuildTaskEntities(IReadOnlyList<CreateProjectTemplateTaskDto>? tasks)
    {
        if (tasks is null || tasks.Count == 0) return [];

        var result = new List<ProjectTemplateTask>(tasks.Count);
        for (var i = 0; i < tasks.Count; i++)
        {
            var t = tasks[i];
            if (string.IsNullOrWhiteSpace(t.Title))
                throw new ArgumentException("Every template task requires a title.", nameof(tasks));

            result.Add(new ProjectTemplateTask
            {
                Id = Guid.NewGuid(),
                Title = t.Title.Trim(),
                Description = t.Description,
                Priority = t.Priority,
                Complexity = t.Complexity,
                DueDateOffsetDays = t.DueDateOffsetDays,
                SortOrder = i
            });
        }

        return result;
    }

    /// <summary>
    /// Seeds a small built-in starter set the first time this user has zero project
    /// templates, so the feature is useful without any setup. If the user later
    /// deletes every project template (built-in and custom alike), the next fetch
    /// seeds the starter set again — a deliberate, documented trade-off in favor of
    /// simplicity over tracking a separate "already seeded" flag per user.
    /// </summary>
    private async Task SeedBuiltInIfEmptyAsync(string userId, CancellationToken cancellationToken)
    {
        var hasAny = await context.ProjectTemplates.AsNoTracking()
            .AnyAsync(t => t.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (hasAny) return;

        foreach (var template in BuiltInTemplateSet.BuildProjectTemplates(userId))
            context.ProjectTemplates.Add(template);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ProjectTemplate> Query(string userId) =>
        context.ProjectTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .Include(t => t.DefaultArea)
            .Include(t => t.DefaultGoal)
            .Where(t => t.UserId == userId);

    private static ProjectTemplateDto ToDto(ProjectTemplate t) => new(
        t.Id,
        t.Name,
        t.ProjectNamePattern,
        t.Description,
        t.DesiredOutcome,
        t.DefaultPriority,
        t.DefaultAreaId,
        t.DefaultArea?.Name,
        t.DefaultGoalId,
        t.DefaultGoal?.Title,
        t.IsBuiltIn,
        t.CreatedAtUtc,
        t.UpdatedAtUtc,
        t.Tasks
            .OrderBy(task => task.SortOrder)
            .Select(task => new ProjectTemplateTaskDto(
                task.Id, task.Title, task.Description, task.Priority, task.Complexity,
                task.DueDateOffsetDays, task.SortOrder))
            .ToList(),
        t.RowVersion);

    private static IReadOnlyCollection<string> ReadTags(Guid? templateId = null)
    {
        List<string> tags = [ApplicationCacheKey.EntityTypeTag<ProjectTemplate>()];
        if (templateId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<ProjectTemplate>(templateId.Value));
        return tags;
    }

    private ValueTask InvalidateAsync(string userId, Guid templateId) =>
        cache.InvalidateTagsAsync(
            userId,
            [ApplicationCacheKey.EntityTypeTag<ProjectTemplate>(), ApplicationCacheKey.EntityTag<ProjectTemplate>(templateId)],
            CancellationToken.None);
}
