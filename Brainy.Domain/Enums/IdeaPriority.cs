namespace Brainy.Domain.Enums;

/// <summary>
/// Relative importance of an <see cref="Entities.Idea"/>.
/// Higher values surface the idea more prominently in review and prioritisation views.
/// Ordering: Critical &gt; High &gt; Medium &gt; Low.
/// </summary>
public enum IdeaPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
