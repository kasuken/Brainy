using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Pulse;

/// <summary>
/// A single entry in the Pulse activity log — one thing the user did (or that happened
/// to one of their items) at a point in time, ordered most-recent-first in the report.
/// </summary>
public record PulseActivityLogEntryDto(
    Guid EntityId,
    PulseActivityType ActivityType,
    DateTime OccurredAtUtc,
    string Title,
    /// <summary>Optional context, e.g. the parent project or note title.</summary>
    string? Context,
    /// <summary>Relative app URL to navigate to the underlying item, when one exists.</summary>
    string? Link);
