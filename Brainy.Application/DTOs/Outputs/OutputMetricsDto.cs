using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Aggregated metrics about the current user's outputs, broken down by status, type, and area.</summary>
public record OutputMetricsDto(
    int TotalOutputs,
    int DraftCount,
    int InReviewCount,
    int ReadyCount,
    int PublishedCount,
    int ArchivedCount,
    IReadOnlyList<OutputsByTypeDto> ByType,
    IReadOnlyList<OutputsByAreaDto> ByArea);

/// <summary>Output count for a single <see cref="OutputType"/>.</summary>
public record OutputsByTypeDto(OutputType Type, int Count);

/// <summary>Output count for a single area (or outputs with no area when <see cref="AreaId"/> is null).</summary>
public record OutputsByAreaDto(Guid? AreaId, string AreaName, int Count);
