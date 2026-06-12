using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Goals;

/// <summary>Read-only projection of a <see cref="Domain.Entities.GoalActivity"/> entry.</summary>
public record GoalActivityDto(
    Guid Id,
    GoalActivityType ActivityType,
    string Description,
    string? OldValue,
    string? NewValue,
    DateTime CreatedAtUtc);
