using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Append-only internal product-analytics event, scoped to the authenticated user who
/// triggered it. This is a distinct concept from <see cref="LifecycleActivity"/>: that
/// ledger is user-facing personal history (the "Pulse" feature); this table is internal
/// product measurement used to answer activation, retrieval, and retention questions.
/// </summary>
/// <remarks>
/// Data-minimisation invariant: <see cref="EventName"/> must come from the fixed constant
/// list in <c>Brainy.Application.Analytics.AnalyticsEvents</c>, never free text supplied by
/// a caller. <see cref="PropertiesJson"/> may only hold non-content metadata — counts,
/// booleans, entity-type names, and entity ids. It must never contain note or task body
/// text, output content, search query text, or any AI prompt or generated text. Violating
/// this invariant would defeat the product's trust proposition of never collecting content
/// for analytics.
/// </remarks>
public sealed class ProductEvent : IUserOwnedEntity
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>One of the constants declared in <c>AnalyticsEvents</c>.</summary>
    public string EventName { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Optional small JSON object of non-content metadata only. See the class-level
    /// remarks for the data-minimisation invariant this column must uphold.
    /// </summary>
    public string? PropertiesJson { get; set; }
}
