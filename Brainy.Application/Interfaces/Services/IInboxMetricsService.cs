using Brainy.Application.DTOs.Inbox;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Computes inbox health statistics for the current user.</summary>
public interface IInboxMetricsService
{
    Task<InboxMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
}
