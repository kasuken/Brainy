using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Search;

/// <summary>
/// A single result from Brainy's cross-entity knowledge search, including a content snippet with the matched
/// excerpt and a relevance score for ordering results.
/// </summary>
public record SearchResultDto(
    Guid Id,
    string Title,
    /// <summary>Up to 200-character excerpt of the content surrounding the match.</summary>
    string ContentSnippet,
    string? AiSummary,
    NoteStatus Status,
    ParaCategory ParaCategory,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? ResourceId,
    DateTime UpdatedAtUtc,
    /// <summary>
    /// Relevance score: 2 = title match, 1 = content-only match.
    /// Higher is more relevant.
    /// </summary>
    int Relevance,
    /// <summary>Discriminator that identifies the source entity.</summary>
    string ResultType = "Note",
    /// <summary>Populated when <see cref="ResultType"/> is "Output".</summary>
    OutputType? OutputType = null,
    /// <summary>Populated when <see cref="ResultType"/> is "Output".</summary>
    OutputStatus? OutputStatus = null,
    /// <summary>Tag names associated with the result, when supported by its entity type.</summary>
    IReadOnlyList<string>? Tags = null,
    /// <summary>The field that produced the snippet, such as Content, Description, Topic, Title, or Tags.</summary>
    string? SnippetSource = null);
