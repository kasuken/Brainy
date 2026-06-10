namespace Brainy.Application.DTOs.Archives;

public record ArchiveRetentionRuleDto(
    Guid Id,
    string EntityType,
    int? RetentionDays);
