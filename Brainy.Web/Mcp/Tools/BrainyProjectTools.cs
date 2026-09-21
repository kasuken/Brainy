using System.ComponentModel;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using ModelContextProtocol.Server;

namespace Brainy.Web.Mcp.Tools;

/// <summary>
/// MCP tools for creating and maintaining the signed-in user's projects. Thin adapters over
/// <see cref="IProjectService"/> and <see cref="IAreaService"/>; every call is scoped to the
/// token's owner. Update is fetch-then-merge, so a caller only supplies the fields it wants to
/// change. There is deliberately no hard-delete tool — archiving is reversible; deletion is not.
/// </summary>
[McpServerToolType]
internal sealed class BrainyProjectTools
{
    [McpServerTool(Name = "list_projects")]
    [Description("List the user's projects with their status, priority and due date. By default " +
                 "returns active, non-archived projects; set includeArchived to see archived ones.")]
    public static async Task<IReadOnlyList<McpProject>> ListProjectsAsync(
        IProjectService projectService,
        [Description("When true, returns archived projects instead of the active ones.")]
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var projects = includeArchived
            ? await projectService.GetAllArchivedAsync(cancellationToken).ConfigureAwait(false)
            : await projectService.GetAllNonArchivedAsync(cancellationToken).ConfigureAwait(false);

        return projects.Select(McpProject.From).ToList();
    }

    [McpServerTool(Name = "get_project")]
    [Description("Get a single project by its id, or null if it does not exist or is not the user's.")]
    public static async Task<McpProject?> GetProjectAsync(
        IProjectService projectService,
        [Description("The project's id.")] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await projectService.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        return project is null ? null : McpProject.From(project);
    }

    [McpServerTool(Name = "list_areas")]
    [Description("List the user's active areas (PARA areas of responsibility). Every project " +
                 "belongs to an area, so use this to obtain a valid areaId before creating a project.")]
    public static async Task<IReadOnlyList<McpArea>> ListAreasAsync(
        IAreaService areaService,
        CancellationToken cancellationToken = default)
    {
        var areas = await areaService.GetAllActiveAsync(cancellationToken).ConfigureAwait(false);
        return areas.Select(a => new McpArea(a.Id, a.Name, a.Description)).ToList();
    }

    [McpServerTool(Name = "create_project")]
    [Description("Create a new project inside an area. Get a valid areaId from list_areas first.")]
    public static async Task<McpProject> CreateProjectAsync(
        IProjectService projectService,
        [Description("The project name.")] string name,
        [Description("The id of the area this project belongs to (see list_areas).")] Guid areaId,
        [Description("Optional description of the project.")] string? description = null,
        [Description("Optional desired outcome / definition of done.")] string? desiredOutcome = null,
        [Description("Priority: Low, Medium, High, or Critical. Defaults to Medium.")] string? priority = null,
        [Description("Optional start date (UTC).")] DateTime? startDate = null,
        [Description("Optional due date (UTC).")] DateTime? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new CreateProjectDto(
            Name: name,
            AreaId: areaId,
            Description: description,
            DesiredOutcome: desiredOutcome,
            Priority: McpToolHelpers.ParseEnum<ProjectPriority>(priority, nameof(priority)) ?? ProjectPriority.Medium,
            StartDate: startDate,
            DueDate: dueDate);

        var created = await projectService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpProject.From(created);
    }

    [McpServerTool(Name = "update_project")]
    [Description("Update fields of an existing project. Only the fields you supply change; omit " +
                 "the rest. Valid status values: NotStarted, Active, Blocked, Parked, Completed.")]
    public static async Task<McpProject> UpdateProjectAsync(
        IProjectService projectService,
        [Description("The id of the project to update.")] Guid projectId,
        [Description("New name, if changing it.")] string? name = null,
        [Description("New description, if changing it.")] string? description = null,
        [Description("New desired outcome, if changing it.")] string? desiredOutcome = null,
        [Description("New status: NotStarted, Active, Blocked, Parked, or Completed.")] string? status = null,
        [Description("New priority: Low, Medium, High, or Critical.")] string? priority = null,
        [Description("New start date (UTC).")] DateTime? startDate = null,
        [Description("New due date (UTC).")] DateTime? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await projectService.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No project with id {projectId} was found for the current user.");

        if (existing.AreaId is not { } areaId)
            throw new InvalidOperationException("This project has no area and cannot be updated through MCP.");

        var dto = new UpdateProjectDto(
            Id: existing.Id,
            Name: name ?? existing.Name,
            AreaId: areaId,
            Description: description ?? existing.Description,
            DesiredOutcome: desiredOutcome ?? existing.DesiredOutcome,
            Status: McpToolHelpers.ParseEnum<ProjectStatus>(status, nameof(status)) ?? existing.Status,
            Priority: McpToolHelpers.ParseEnum<ProjectPriority>(priority, nameof(priority)) ?? existing.Priority,
            StartDate: startDate ?? existing.StartDate,
            DueDate: dueDate ?? existing.DueDate,
            GoalId: existing.GoalId,
            Emoji: existing.Emoji,
            RowVersion: existing.RowVersion);

        var updated = await projectService.UpdateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpProject.From(updated);
    }

    [McpServerTool(Name = "complete_project")]
    [Description("Mark a project as completed. openTaskAction decides what happens to its still-open " +
                 "tasks: LeaveAsIs (default), CompleteAll, or ArchiveAll.")]
    public static async Task<McpProject> CompleteProjectAsync(
        IProjectService projectService,
        [Description("The id of the project to complete.")] Guid projectId,
        [Description("What to do with open tasks: LeaveAsIs, CompleteAll, or ArchiveAll.")] string? openTaskAction = null,
        CancellationToken cancellationToken = default)
    {
        var action = McpToolHelpers.ParseEnum<TaskCompletionAction>(openTaskAction, nameof(openTaskAction))
                     ?? TaskCompletionAction.LeaveAsIs;
        var completed = await projectService.CompleteAsync(projectId, action, cancellationToken).ConfigureAwait(false);
        return McpProject.From(completed);
    }

    [McpServerTool(Name = "archive_project")]
    [Description("Archive a project (reversible). It leaves active work views but is kept and can be restored.")]
    public static async Task<string> ArchiveProjectAsync(
        IProjectService projectService,
        [Description("The id of the project to archive.")] Guid projectId,
        [Description("Optional reason recorded with the archive.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await projectService.ArchiveAsync(projectId, cancellationToken, reason).ConfigureAwait(false);
        return $"Project {projectId} archived.";
    }
}

/// <summary>Compact project projection for MCP callers.</summary>
public record McpProject(
    Guid Id,
    string Name,
    [property: Description("NotStarted, Active, Blocked, Parked, Completed, or Archived.")]
    string Status,
    [property: Description("Low, Medium, High, or Critical.")]
    string Priority,
    DateTime? DueDate,
    bool IsArchived,
    Guid? AreaId,
    string? Description,
    string? DesiredOutcome)
{
    public static McpProject From(ProjectDto p) => new(
        p.Id,
        p.Name,
        p.Status.ToString(),
        p.Priority.ToString(),
        p.DueDate,
        p.IsArchived,
        p.AreaId,
        p.Description,
        p.DesiredOutcome);
}

/// <summary>Compact area projection, enough to pick an areaId for a new project.</summary>
public record McpArea(Guid Id, string Name, string? Description);
