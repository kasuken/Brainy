using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Backs the Resume Context panel (issue #305): a compact, deterministic summary of what
/// a user needs to restart a task, sourced entirely from data they already own. Never
/// calls into any AI provider.
/// </summary>
public interface IResumeContextService
{
    /// <summary>
    /// Builds the Resume Context read model for a task owned by the current user.
    /// Returns null when the task does not exist or is not owned by the current user.
    /// </summary>
    Task<ResumeContextDto?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or edits the user-authored restart note on a task owned by the current user.
    /// Pass null or whitespace to clear it. Throws <see cref="KeyNotFoundException"/> when
    /// the task does not exist or is not owned by the current user.
    /// </summary>
    Task<ResumeContextDto> SaveRestartNoteAsync(
        Guid taskId,
        string? restartNote,
        CancellationToken cancellationToken = default);
}
