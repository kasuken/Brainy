using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Input data required to update an existing <see cref="Domain.Entities.Output"/>.</summary>
public record UpdateOutputDto(
    Guid Id,
    string Title,
    string? Description,
    OutputType Type,
    OutputStatus Status,
    string Content,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? GoalId);
