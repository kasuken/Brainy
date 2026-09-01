using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Stores note attachments as binary in the database, scoped to the current user.
/// Reads use <c>AsNoTracking</c>; the serving path takes an explicit user id because it
/// runs outside a Blazor circuit.
/// </summary>
internal sealed class NoteImageService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IApplicationCache cache) : INoteImageService
{
    private const int UploadLockStripeCount = 64;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private static readonly SemaphoreSlim[] UploadLocks = Enumerable
        .Range(0, UploadLockStripeCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/bmp",
        "application/pdf"
    };

    public async Task<NoteImageDto> UploadAsync(UploadNoteImageDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Data is null || dto.Data.Length == 0)
            throw new ArgumentException("Attachment data is empty.", nameof(dto));

        if (dto.Data.Length > INoteImageService.MaxSizeBytes)
            throw new ArgumentException(
                $"Attachment exceeds the maximum allowed size of {INoteImageService.MaxSizeBytes / (1024 * 1024)} MB.",
                nameof(dto));

        var contentType = dto.ContentType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
            throw new ArgumentException($"Unsupported attachment type '{dto.ContentType}'.", nameof(dto));

        if (!HasValidSignature(dto.Data, contentType))
            throw new ArgumentException(
                $"Attachment bytes do not match the declared content type '{dto.ContentType}'.",
                nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Notes.EnsureOwnedAsync(dto.NoteId, userId, "Note", cancellationToken)
            .ConfigureAwait(false);

        var uploadLock = GetUploadLock(userId);
        await uploadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DeleteExpiredPendingAsync(userId, cancellationToken).ConfigureAwait(false);

            var usedBytes = await context.NoteImages
                .AsNoTracking()
                .Where(image => image.UserId == userId)
                .SumAsync(image => (long?)image.SizeBytes, cancellationToken)
                .ConfigureAwait(false) ?? 0;

            if (usedBytes > INoteImageService.MaxUserStorageBytes - dto.Data.LongLength)
                throw new InvalidOperationException(
                    $"Attachment storage quota exceeded. Each account can store up to " +
                    $"{INoteImageService.MaxUserStorageBytes / (1024 * 1024)} MB of attachments.");

            var fileName = string.IsNullOrWhiteSpace(dto.FileName) ? "attachment" : dto.FileName.Trim();
            if (fileName.Length > 255)
                fileName = fileName[..255];

            var image = new NoteImage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NoteId = dto.NoteId,
                FileName = fileName,
                ContentType = contentType,
                SizeBytes = dto.Data.LongLength,
                Data = dto.Data
            };

            context.NoteImages.Add(image);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateImagesAsync(userId, [image.Id]).ConfigureAwait(false);

            return new NoteImageDto(image.Id, image.NoteId, image.FileName, image.ContentType, image.SizeBytes);
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<NoteImageContentDto?> GetContentAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        return await context.NoteImages
            .AsNoTracking()
            .Where(i => i.Id == id && i.UserId == userId)
            .Select(i => new NoteImageContentDto(i.Data, i.ContentType, i.FileName))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> AssociateWithNoteAsync(Guid noteId, IEnumerable<Guid> imageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageIds);

        var ids = imageIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Notes.EnsureOwnedAsync(noteId, userId, "Note", cancellationToken)
            .ConfigureAwait(false);

        var uploadLock = GetUploadLock(userId);
        await uploadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updated = await context.NoteImages
                .Where(i => i.UserId == userId && ids.Contains(i.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.NoteId, noteId), cancellationToken)
                .ConfigureAwait(false);

            // ExecuteUpdate bypasses EF's change tracker. Detach matching uploads so a
            // long-lived Blazor context cannot later mistake its stale NoteId == null value
            // for a still-pending image and delete an image that is already attached.
            foreach (var tracked in context.NoteImages.Local
                         .Where(image => image.UserId == userId && ids.Contains(image.Id))
                         .ToList())
            {
                context.Entry(tracked).State = EntityState.Detached;
            }

            if (updated > 0)
                await InvalidateImagesAsync(userId, ids).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<IReadOnlyList<NoteImageDto>> GetForNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Notes.EnsureOwnedAsync(noteId, userId, "Note", cancellationToken)
            .ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"note-images:note:{noteId}",
            [ApplicationCacheKey.EntityTypeTag<NoteImage>()],
            async ct => await context.NoteImages
                .AsNoTracking()
                .Where(image => image.UserId == userId && image.NoteId == noteId)
                .OrderByDescending(image => image.CreatedAtUtc)
                .ThenByDescending(image => image.Id)
                .Select(image => new NoteImageDto(
                    image.Id,
                    image.NoteId,
                    image.FileName,
                    image.ContentType,
                    image.SizeBytes))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteAsync(
        IEnumerable<Guid> imageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageIds);

        var ids = imageIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var uploadLock = GetUploadLock(userId);
        await uploadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ownedImages = await context.NoteImages
                .Where(image => image.UserId == userId && ids.Contains(image.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ownedImages.Count == 0)
                return 0;

            context.NoteImages.RemoveRange(ownedImages);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateImagesAsync(userId, ownedImages.Select(image => image.Id)).ConfigureAwait(false);
            return ownedImages.Count;
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<int> DeletePendingAsync(
        IEnumerable<Guid> imageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageIds);

        var ids = imageIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var uploadLock = GetUploadLock(userId);
        await uploadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = await GetDeletablePendingAsync(
                image => image.UserId == userId && image.NoteId == null && ids.Contains(image.Id),
                cancellationToken).ConfigureAwait(false);

            if (pending.Count == 0)
                return 0;

            context.NoteImages.RemoveRange(pending);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateImagesAsync(userId, pending.Select(image => image.Id)).ConfigureAwait(false);
            return pending.Count;
        }
        finally
        {
            uploadLock.Release();
        }
    }

    private async Task DeleteExpiredPendingAsync(string userId, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - INoteImageService.UnattachedRetention;
        var expired = await GetDeletablePendingAsync(
            image => image.UserId == userId && image.NoteId == null && image.CreatedAtUtc < cutoff,
            cancellationToken).ConfigureAwait(false);

        if (expired.Count == 0)
            return;

        context.NoteImages.RemoveRange(expired);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateImagesAsync(userId, expired.Select(image => image.Id)).ConfigureAwait(false);
    }

    private ValueTask InvalidateImagesAsync(string userId, IEnumerable<Guid> imageIds)
    {
        List<string> tags = [ApplicationCacheKey.EntityTypeTag<NoteImage>()];
        tags.AddRange(imageIds.Select(ApplicationCacheKey.EntityTag<NoteImage>));
        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private async Task<List<NoteImage>> GetDeletablePendingAsync(
        System.Linq.Expressions.Expression<Func<NoteImage, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var identities = await context.NoteImages
            .AsNoTracking()
            .Where(predicate)
            .Select(image => new PendingImageIdentity(image.Id, image.RowVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = new List<NoteImage>(identities.Count);
        foreach (var identity in identities)
        {
            var image = context.NoteImages.Local.FirstOrDefault(candidate => candidate.Id == identity.Id)
                ?? new NoteImage { Id = identity.Id, RowVersion = identity.RowVersion };

            // A tracked entity may have been attached since the database projection ran.
            if (image.NoteId is null)
                pending.Add(image);
        }

        return pending;
    }

    private static SemaphoreSlim GetUploadLock(string userId)
    {
        var hash = StringComparer.Ordinal.GetHashCode(userId) & int.MaxValue;
        return UploadLocks[hash % UploadLockStripeCount];
    }

    private static bool HasValidSignature(byte[] data, string contentType) => contentType switch
    {
        "image/png" => data.AsSpan().StartsWith(PngSignature),
        "image/jpeg" => data.AsSpan().StartsWith(JpegSignature),
        "image/gif" => data.AsSpan().StartsWith("GIF87a"u8) || data.AsSpan().StartsWith("GIF89a"u8),
        "image/webp" => data.Length >= 12
            && data.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && data.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        "image/bmp" => data.AsSpan().StartsWith("BM"u8),
        "application/pdf" => data.AsSpan().StartsWith(PdfSignature),
        _ => false
    };

    private sealed record PendingImageIdentity(Guid Id, byte[]? RowVersion);
}
