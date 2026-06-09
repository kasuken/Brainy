namespace Brainy.Domain.Enums;

/// <summary>
/// Relative importance of a <see cref="Entities.Project"/>.
/// Higher values surface the project and its tasks more prominently on Today,
/// in the Project List, and on Project Details.
/// Ordering: Critical &gt; High &gt; Medium &gt; Low.
/// </summary>
public enum ProjectPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
