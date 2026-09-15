namespace Brainy.Application.DTOs.Push;

/// <summary>Summary of one background dispatch pass, for logging and tests.</summary>
public record PushDispatchRunResultDto(
    int UsersEvaluated,
    int NotificationsSent,
    int SubscriptionsPruned);
