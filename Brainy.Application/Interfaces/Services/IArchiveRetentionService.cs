using Brainy.Application.DTOs.Archives;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Manages per-user archive retention policies.</summary>
public interface IArchiveRetentionService
{
    Task<IReadOnlyList<ArchiveRetentionRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task UpsertRuleAsync(string entityType, int? retentionDays, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(string entityType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans all users' archived items and permanently deletes those that exceed the configured
    /// retention period. Returns the total number of items purged.
    /// </summary>
    Task<int> EnforceRetentionAsync(CancellationToken cancellationToken = default);
}
