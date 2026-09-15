using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Templates;

/// <summary>One task entry within a project template's default task list.</summary>
public record ProjectTemplateTaskDto(
    Guid Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    TaskComplexity? Complexity,
    int? DueDateOffsetDays,
    int SortOrder);

/// <summary>Payload for one task entry when creating or replacing a project template's task list.</summary>
public record CreateProjectTemplateTaskDto(
    string Title,
    string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    TaskComplexity? Complexity = null,
    int? DueDateOffsetDays = null,
    int SortOrder = 0);

/// <summary>Read-only projection of a <see cref="Domain.Entities.ProjectTemplate"/>.</summary>
public record ProjectTemplateDto(
    Guid Id,
    string Name,
    string ProjectNamePattern,
    string? Description,
    string? DesiredOutcome,
    ProjectPriority DefaultPriority,
    Guid? DefaultAreaId,
    string? DefaultAreaName,
    Guid? DefaultGoalId,
    string? DefaultGoalTitle,
    bool IsBuiltIn,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ProjectTemplateTaskDto> Tasks,
    byte[]? RowVersion = null);

/// <summary>Payload for creating a new project template.</summary>
public record CreateProjectTemplateDto(
    string Name,
    string ProjectNamePattern,
    string? Description = null,
    string? DesiredOutcome = null,
    ProjectPriority DefaultPriority = ProjectPriority.Medium,
    Guid? DefaultAreaId = null,
    Guid? DefaultGoalId = null,
    IReadOnlyList<CreateProjectTemplateTaskDto>? Tasks = null);

/// <summary>
/// Payload for updating a project template. The task list is replaced wholesale —
/// callers resend the complete desired set rather than diffing individual entries.
/// </summary>
public record UpdateProjectTemplateDto(
    Guid Id,
    string Name,
    string ProjectNamePattern,
    string? Description = null,
    string? DesiredOutcome = null,
    ProjectPriority DefaultPriority = ProjectPriority.Medium,
    Guid? DefaultAreaId = null,
    Guid? DefaultGoalId = null,
    IReadOnlyList<CreateProjectTemplateTaskDto>? Tasks = null,
    byte[]? RowVersion = null);

/// <summary>
/// Instantiates a project template into a real project. Any supplied override replaces
/// the template's default for that field; omitted values fall back to the template.
/// Subject to the normal active-project entitlement check, exactly like a hand-made
/// project (see <see cref="Common.PlanEntitlementDeniedException"/>).
/// </summary>
public record InstantiateProjectTemplateDto(
    Guid TemplateId,
    string? Name = null,
    Guid? AreaId = null,
    Guid? GoalId = null,
    DateTime? StartDate = null,
    DateTime? DueDate = null);

/// <summary>Saves an existing project (and its open task list) as a reusable template.</summary>
public record SaveProjectAsTemplateDto(Guid ProjectId, string TemplateName);
