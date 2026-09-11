using Brainy.Application.DTOs.Capture;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Services;

/// <summary>
/// Default (and, per issue #302's actual client-side design, permanent) <see cref="IOfflineCaptureQueue"/>
/// registration — see that interface's remarks for why. Always reports that nothing was
/// queued so nothing can mistakenly tell a disconnected user their capture was saved through
/// this seam.
/// </summary>
internal sealed class NullOfflineCaptureQueue : IOfflineCaptureQueue
{
    public Task<bool> TryQueueAsync(ShareCaptureDto dto, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
