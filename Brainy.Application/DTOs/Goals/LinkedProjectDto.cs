using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Goals;

public record LinkedProjectDto(Guid Id, string Name, ProjectStatus Status);
