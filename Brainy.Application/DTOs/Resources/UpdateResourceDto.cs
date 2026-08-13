namespace Brainy.Application.DTOs.Resources;

/// <summary>Payload for updating an existing resource.</summary>
public record UpdateResourceDto(
    Guid Id,
    string Name,
    string? Description,
    string? Topic,
    Guid? AreaId,
    IReadOnlyList<string>? Tags,
    string? Emoji = null,
    /// <summary>
    /// Concurrency token from the loaded resource. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the resource changed after it was loaded.
    /// </summary>
    byte[]? RowVersion = null);
