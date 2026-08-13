using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// Immutable record of a user-visible lifecycle transition. Unlike entity status
/// timestamps, these rows are never cleared by reopen or restore operations.
/// </summary>
public sealed class LifecycleActivity : IUserOwnedEntity
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public PulseActivityType ActivityType { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Context { get; set; }

    public string? Link { get; set; }
}
