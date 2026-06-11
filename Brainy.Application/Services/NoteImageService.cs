using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Stores note images as binary in the database, scoped to the current user.
/// Reads use <c>AsNoTracking</c>; the serving path takes an explicit user id because it
/// runs outside a Blazor circuit.
/// </summary>
internal sealed class NoteImageService(IApplicationDbContext context, ICurrentUserService currentUser) : INoteImageService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/bmp",
        "image/svg+xml"
    };

    public async Task<NoteImageDto> UploadAsync(UploadNoteImageDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Data is null || dto.Data.Length == 0)
            throw new ArgumentException("Image data is empty.", nameof(dto));

        if (dto.Data.Length > INoteImageService.MaxSizeBytes)
            throw new ArgumentException(
                $"Image exceeds the maximum allowed size of {INoteImageService.MaxSizeBytes / (1024 * 1024)} MB.",
                nameof(dto));

        if (string.IsNullOrWhiteSpace(dto.ContentType) || !AllowedContentTypes.Contains(dto.ContentType))
            throw new ArgumentException($"Unsupported image type '{dto.ContentType}'.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var fileName = string.IsNullOrWhiteSpace(dto.FileName) ? "image" : dto.FileName.Trim();
        if (fileName.Length > 255)
            fileName = fileName[..255];

        var image = new NoteImage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NoteId = dto.NoteId,
            FileName = fileName,
            ContentType = dto.ContentType,
            SizeBytes = dto.Data.Length,
            Data = dto.Data
        };

        context.NoteImages.Add(image);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new NoteImageDto(image.Id, image.NoteId, image.FileName, image.ContentType, image.SizeBytes);
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

        return await context.NoteImages
            .Where(i => i.UserId == userId && ids.Contains(i.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.NoteId, noteId), cancellationToken)
            .ConfigureAwait(false);
    }
}
