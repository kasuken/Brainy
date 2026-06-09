namespace Brainy.Domain.Entities;

/// <summary>
/// Base type for all persisted entities. Provides a surrogate key and audit timestamps.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
