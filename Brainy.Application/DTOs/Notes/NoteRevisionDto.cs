using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Read-only projection of a <see cref="Domain.Entities.NoteRevision"/>.</summary>
public record NoteRevisionDto(
    Guid Id,
    Guid NoteId,
    string Title,
    string Content,
    NoteRevisionReason Reason,
    DateTime CreatedAtUtc,
    /// <summary>The AI model used, when <see cref="Reason"/> is <see cref="NoteRevisionReason.AiGeneration"/>.</summary>
    string? Model = null,
    /// <summary>The versioned prompt used, when <see cref="Reason"/> is <see cref="NoteRevisionReason.AiGeneration"/>.</summary>
    string? PromptVersion = null,
    /// <summary>The revision this one was restored from, when <see cref="Reason"/> is <see cref="NoteRevisionReason.Restore"/>.</summary>
    Guid? RestoredFromRevisionId = null);
