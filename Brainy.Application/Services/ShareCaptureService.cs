using Brainy.Application.DTOs.Capture;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IShareCaptureService"/> on top of the existing <see cref="INoteService"/>
/// creation path, so the minimal capture route reuses the same Note/Source invariants (and the
/// same capture analytics events) as the Quick Capture dialog rather than duplicating them.
/// </summary>
internal sealed class ShareCaptureService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    INoteService noteService,
    TimeProvider timeProvider) : IShareCaptureService
{
    public async Task<ShareCaptureResultDto> CaptureAsync(ShareCaptureDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var title = NormalizeTitle(dto.Title);
        var text = dto.Text?.Trim() ?? string.Empty;
        var url = NormalizeUrl(dto.Url);

        if (title is null && text.Length == 0 && url is null)
            throw new ArgumentException("Share a link or some text to capture.", nameof(dto));

        if (text.Length > IShareCaptureService.MaxTextLength)
            throw new ArgumentException(
                $"Captured text cannot exceed {IShareCaptureService.MaxTextLength:N0} characters.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var resolvedTitle = ResolveTitle(title, text, url);

        var duplicateId = await FindRecentDuplicateIdAsync(userId, resolvedTitle, text, url, cancellationToken)
            .ConfigureAwait(false);
        if (duplicateId is { } existingId)
        {
            var existing = await noteService.GetByIdAsync(existingId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The matching recent capture could not be re-read.");
            return new ShareCaptureResultDto(existing, ShareCaptureOutcome.DuplicateIgnored);
        }

        var created = await noteService.CreateAsync(
            new CreateNoteDto(
                resolvedTitle,
                text,
                ParaCategory.Project,
                Status: NoteStatus.Inbox,
                SourceUrl: url,
                SourceTitle: title,
                SourceType: url is not null ? SourceType.SharedLink : null),
            cancellationToken).ConfigureAwait(false);

        return new ShareCaptureResultDto(created, ShareCaptureOutcome.Created);
    }

    /// <summary>Trims and length-checks a shared/typed title. Returns null when blank.</summary>
    private static string? NormalizeTitle(string? rawTitle)
    {
        var trimmed = rawTitle?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > IShareCaptureService.MaxTitleLength)
            throw new ArgumentException(
                $"Title cannot exceed {IShareCaptureService.MaxTitleLength} characters.", nameof(rawTitle));

        return trimmed;
    }

    /// <summary>Trims, length-checks and scheme-validates a shared URL. Returns null when blank.</summary>
    private static string? NormalizeUrl(string? rawUrl)
    {
        var trimmed = rawUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > IShareCaptureService.MaxUrlLength)
            throw new ArgumentException(
                $"URL cannot exceed {IShareCaptureService.MaxUrlLength} characters.", nameof(rawUrl));

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Only http(s) links can be captured.", nameof(rawUrl));

        return uri.ToString();
    }

    private static string ResolveTitle(string? title, string text, string? url)
    {
        if (title is not null) return title;

        if (text.Length > 0)
        {
            var firstLine = text.Split('\n', 2)[0].Trim();
            if (firstLine.Length > 0)
                return firstLine.Length > IShareCaptureService.MaxTitleLength
                    ? firstLine[..IShareCaptureService.MaxTitleLength].TrimEnd() + "…"
                    : firstLine;
        }

        if (url is not null)
            return CleanUrlForTitle(url);

        return "Untitled capture";
    }

    /// <summary>Produces a tidy, scheme-less label from a URL for use as a note title.</summary>
    private static string CleanUrlForTitle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var display = (uri.Host + uri.PathAndQuery).TrimEnd('/');
        return display.Length <= IShareCaptureService.MaxTitleLength
            ? display
            : display[..IShareCaptureService.MaxTitleLength].TrimEnd() + "…";
    }

    /// <summary>
    /// Looks up a note owned by the current user, created within <see cref="IShareCaptureService.DuplicateWindow"/>,
    /// with the same resolved title, text and (normalized) source URL — a same-user retry of an
    /// identical capture (OS share-sheet retry, or this route's own prerender-then-interactive re-run).
    /// This is a best-effort window check rather than an atomic idempotency key: sufficient for the
    /// near-simultaneous retries this route needs to absorb, without a schema migration.
    /// </summary>
    private async Task<Guid?> FindRecentDuplicateIdAsync(
        string userId, string title, string text, string? url, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - IShareCaptureService.DuplicateWindow;

        var query = context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.CreatedAtUtc >= cutoff && n.Title == title && n.Content == text);

        query = url is null
            ? query.Where(n => n.SourceId == null || n.Source!.Url == null)
            : query.Where(n => n.Source != null && n.Source.Url == url);

        return await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
