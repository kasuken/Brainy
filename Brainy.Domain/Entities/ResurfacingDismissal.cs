using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Records that a user permanently dismissed a note from future weekly-review
/// resurfacing suggestions. This is distinct from the review's "Keep" decision,
/// which only clears the current review's prompt without suppressing the note
/// from a future review's recency-based resurfacing.
/// </summary>
public sealed class ResurfacingDismissal : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The note that was dismissed from resurfacing.</summary>
    public Guid NoteId { get; set; }

    /// <summary>Navigation to the dismissed note.</summary>
    public Note Note { get; set; } = null!;

    /// <summary>When the user dismissed this note from future resurfacing.</summary>
    public DateTime DismissedAtUtc { get; set; }
}
