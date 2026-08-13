using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Ideas;

/// <summary>
/// Full projection of an <see cref="Domain.Entities.Idea"/>, including research and competitor notes.
/// Used on detail/edit views.
/// </summary>
public record IdeaDetailDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AreaId,
    string? AreaName,
    IdeaPriority Priority,
    IdeaStatus Status,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? Research,
    string? Competitors,
    string? Notes,
    string? TargetUserAndProblem,
    string? SuitabilityReason,
    string? Evidence,
    string? ValidationExperiment,
    string? ReplacedCommitment,
    Guid? CommittedProjectId,
    DateTime? CommittedAtUtc,
    /// <summary>Concurrency token captured at load time; pass back on update or delete.</summary>
    byte[]? RowVersion = null);
