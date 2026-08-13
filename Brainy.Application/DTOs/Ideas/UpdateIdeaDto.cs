using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Ideas;

/// <summary>Payload for updating an existing <see cref="Domain.Entities.Idea"/>.</summary>
public record UpdateIdeaDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AreaId,
    IdeaPriority Priority,
    IdeaStatus Status,
    string? Research,
    string? Competitors,
    string? Notes,
    string? TargetUserAndProblem = null,
    string? SuitabilityReason = null,
    string? Evidence = null,
    string? ValidationExperiment = null,
    string? ReplacedCommitment = null,
    /// <summary>
    /// Concurrency token from the loaded idea. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the idea changed after it was loaded.
    /// </summary>
    byte[]? RowVersion = null);
