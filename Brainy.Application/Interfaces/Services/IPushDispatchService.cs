using Brainy.Application.DTOs.Push;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Evaluates and sends due push notifications for every opted-in user. Intended to be called
/// periodically by a singleton hosted background service (see
/// <c>Brainy.Web.BackgroundServices.PushNotificationDispatchBackgroundService</c>); each call
/// re-evaluates from persisted state, so a run that is interrupted (app restart, deploy) loses
/// nothing — the next call picks up exactly where the frequency-cap log left off.
/// </summary>
public interface IPushDispatchService
{
    Task<PushDispatchRunResultDto> DispatchDueNotificationsAsync(CancellationToken cancellationToken = default);
}
