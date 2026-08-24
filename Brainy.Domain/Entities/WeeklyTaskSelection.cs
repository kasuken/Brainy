using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Explicitly records that a user chose a top-level task as part of a specific
/// Monday-Sunday planning week. The <see cref="WeekStartDate"/> is always the
/// normalized Monday date in the user's calendar.
/// </summary>
public sealed class WeeklyTaskSelection : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The selected task.</summary>
    public Guid TaskId { get; set; }

    /// <summary>Navigation to the selected task.</summary>
    public TaskItem Task { get; set; } = null!;

    /// <summary>
    /// Monday date that identifies the planning week. Stored as a user-calendar
    /// date rather than a UTC instant.
    /// </summary>
    public DateTime WeekStartDate { get; set; }
}
