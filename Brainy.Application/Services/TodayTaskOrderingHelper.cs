using Brainy.Domain.Entities;

namespace Brainy.Application.Services;

/// <summary>
/// Shared ordering extension for Today-screen queries.
/// Encapsulates the canonical sort so every consumer applies the same precedence.
/// </summary>
internal static class TodayTaskOrderingHelper
{
    /// <summary>
    /// Applies canonical Today sort: overdue → due today → critical → high → medium → low → due date.
    /// </summary>
    internal static IQueryable<TaskItem> ApplyTodayOrder(this IQueryable<TaskItem> query, DateTime today)
    {
        return query
            .OrderByDescending(t => t.DueDate.HasValue && t.DueDate.Value.Date < today)
            .ThenByDescending(t => t.DueDate.HasValue && t.DueDate.Value.Date == today)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate);
    }
}
