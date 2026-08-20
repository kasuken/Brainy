namespace Brainy.Application.DTOs.Ideas;

/// <summary>
/// Captures the decision record required to turn an idea into a project.
/// </summary>
/// <param name="Id">The idea to commit.</param>
/// <param name="TargetUserAndProblem">The specific user and problem the project will address.</param>
/// <param name="SuitabilityReason">Why the owner is suited to pursue the idea.</param>
/// <param name="Evidence">Real evidence that supports pursuing the idea.</param>
/// <param name="ValidationExperiment">A small experiment that can validate the idea.</param>
/// <param name="ReplacedCommitment">The existing commitment that will be stopped or reduced.</param>
/// <param name="RowVersion">The concurrency token captured when the idea was loaded.</param>
public sealed record CommitIdeaToProjectDto(
    Guid Id,
    string? TargetUserAndProblem,
    string? SuitabilityReason,
    string? Evidence,
    string? ValidationExperiment,
    string? ReplacedCommitment,
    byte[]? RowVersion = null);
