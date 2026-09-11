using Brainy.Application.DTOs.Capture;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Backs Brainy's minimal capture route (<c>/capture/share</c>), used both as the PWA's Web
/// Share Target handler and as a super-minimal manual "type something and save" page. Creates
/// exactly one Inbox <see cref="Domain.Entities.Note"/> per distinct capture, scoped to the
/// current user, keeping the user's own text separate from any shared-link
/// <see cref="Domain.Entities.Source"/> metadata.
/// </summary>
public interface IShareCaptureService
{
    /// <summary>Maximum length for a shared/entered title. Matches the Note/Source title column width.</summary>
    const int MaxTitleLength = 500;

    /// <summary>Maximum length for a shared URL. Matches the Source url column width.</summary>
    const int MaxUrlLength = 2048;

    /// <summary>Maximum length for shared or typed capture text.</summary>
    const int MaxTextLength = 20_000;

    /// <summary>
    /// Window within which an identical share (same user, same title/text/url) is treated as a
    /// retry of the same capture rather than a new one.
    /// </summary>
    static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Validates and saves one capture. Throws <see cref="ArgumentException"/> when nothing
    /// capturable was supplied, or when the title/text/url exceed the limits above. When a
    /// matching capture from the current user was saved within <see cref="DuplicateWindow"/>,
    /// that existing note is returned instead of creating a duplicate.
    /// </summary>
    Task<ShareCaptureResultDto> CaptureAsync(ShareCaptureDto dto, CancellationToken cancellationToken = default);
}
