using Brainy.Application.Caching;
using Brainy.Application.DTOs.NoteRelationships;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="NoteRelationship"/> records for the current user.
/// Both notes must be owned by the current user.
/// </summary>
internal sealed class NoteRelationshipService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : INoteRelationshipService
{
    public async Task<IReadOnlyList<NoteRelationshipDto>> GetForNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"note-relationships:note:{noteId}",
            [
                ApplicationCacheKey.EntityTypeTag<NoteRelationship>(),
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(noteId)
            ],
            ct => GetForNoteCoreAsync(noteId, userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<NoteRelationshipDto>> GetForNoteCoreAsync(
        Guid noteId,
        string userId,
        CancellationToken cancellationToken)
    {
        // Outgoing: this note is the source
        var outgoing = await context.NoteRelationships
            .AsNoTracking()
            .Where(r => r.SourceNoteId == noteId && r.SourceNote.UserId == userId)
            .Select(r => new NoteRelationshipDto(
                r.Id,
                r.TargetNoteId,
                r.TargetNote.Title,
                r.Type,
                true,
                r.Annotation,
                r.IsAiGenerated))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Incoming: this note is the target
        var incoming = await context.NoteRelationships
            .AsNoTracking()
            .Where(r => r.TargetNoteId == noteId && r.TargetNote.UserId == userId)
            .Select(r => new NoteRelationshipDto(
                r.Id,
                r.SourceNoteId,
                r.SourceNote.Title,
                r.Type,
                false,
                r.Annotation,
                r.IsAiGenerated))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. outgoing, .. incoming];
    }

    public async Task<NoteRelationshipDto> CreateAsync(
        CreateNoteRelationshipDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.SourceNoteId == dto.TargetNoteId)
            throw new InvalidOperationException("A note cannot be linked to itself.");

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Ensure both notes exist and belong to the current user
        var sourceNote = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == dto.SourceNoteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Source note '{dto.SourceNoteId}' was not found.");

        var targetNote = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == dto.TargetNoteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target note '{dto.TargetNoteId}' was not found.");

        // Prevent duplicate in the same direction
        var exists = await context.NoteRelationships
            .AnyAsync(r => r.SourceNoteId == dto.SourceNoteId
                        && r.TargetNoteId == dto.TargetNoteId
                        && r.Type == dto.Type, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
            throw new InvalidOperationException("This relationship already exists.");

        var relationship = new NoteRelationship
        {
            Id           = Guid.NewGuid(),
            SourceNoteId = dto.SourceNoteId,
            TargetNoteId = dto.TargetNoteId,
            Type         = dto.Type,
            Annotation   = dto.Annotation,
            IsAiGenerated = false
        };

        context.NoteRelationships.Add(relationship);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateRelationshipAsync(userId, relationship.Id).ConfigureAwait(false);

        return new NoteRelationshipDto(
            relationship.Id,
            targetNote.Id,
            targetNote.Title,
            relationship.Type,
            true,
            relationship.Annotation,
            false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Allow deletion if the user owns either the source or target note
        var rel = await context.NoteRelationships
            .FirstOrDefaultAsync(r => r.Id == id
                && (r.SourceNote.UserId == userId || r.TargetNote.UserId == userId),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Relationship '{id}' was not found.");

        context.NoteRelationships.Remove(rel);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateRelationshipAsync(userId, rel.Id).ConfigureAwait(false);
    }

    private ValueTask InvalidateRelationshipAsync(string userId, Guid relationshipId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<NoteRelationship>(),
                ApplicationCacheKey.EntityTag<NoteRelationship>(relationshipId)
            ],
            CancellationToken.None);
}
