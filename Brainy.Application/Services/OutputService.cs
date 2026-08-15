using Brainy.Application.Common;
using Brainy.Application.DTOs.Outputs;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD, archiving, publishing, and source-note management for
/// <see cref="Output"/> entities, scoped to the current user.
/// Active outputs exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class OutputService(IApplicationDbContext context, ICurrentUserService currentUser) : IOutputService
{
    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OutputDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && !o.IsArchived)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutputDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && o.IsArchived)
            .OrderByDescending(o => o.ArchivedDate)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutputDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && !o.IsArchived && o.ProjectId == projectId)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutputDto>> GetByGoalAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && !o.IsArchived && o.GoalId == goalId)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutputDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && !o.IsArchived && o.AreaId == areaId)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutputDto>> GetBySourceNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId &&
                        o.SourceNotes.Any(n => n.Id == noteId && n.UserId == userId))
            .OrderBy(o => o.IsArchived)
            .ThenByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.Id == id && o.UserId == userId)
            .Select(o => ToDto(o))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs.AsNoTracking()
            .Include(o => o.Project)
            .Include(o => o.Area)
            .Include(o => o.Goal)
            .Include(o => o.SourceNotes)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false);

        if (output is null)
            return null;

        var sourceNotes = output.SourceNotes
            .Select(n => new OutputSourceNoteDto(n.Id, n.Title, n.CreatedAtUtc))
            .ToList();

        return new OutputDetailDto(
            output.Id,
            output.Title,
            output.Description,
            output.Content,
            output.Type,
            output.Status,
            output.IsAiGenerated,
            output.Model,
            output.PromptVersion,
            output.IsArchived,
            output.ProjectId,
            output.Project?.Name,
            output.AreaId,
            output.Area?.Name,
            output.GoalId,
            output.Goal?.Title,
            output.PublishedDate,
            output.ArchivedDate,
            output.CreatedAtUtc,
            output.UpdatedAtUtc,
            sourceNotes,
            output.RowVersion,
            output.ArchivedReason);
    }

    public async Task<IReadOnlyList<OutputDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var term = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
            return await GetAllActiveAsync(cancellationToken).ConfigureAwait(false);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && !o.IsArchived &&
                        (o.Title.Contains(term) ||
                         (o.Description != null && o.Description.Contains(term)) ||
                         o.Content.Contains(term)))
            .OrderByDescending(o => o.Title.Contains(term) ? 1 : 0)
            .ThenByDescending(o => o.UpdatedAtUtc)
            .Select(o => ToDto(o))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var total     = await context.Outputs.CountAsync(o => o.UserId == userId, cancellationToken).ConfigureAwait(false);
        var draft     = await context.Outputs.CountAsync(o => o.UserId == userId && o.Status == OutputStatus.Draft, cancellationToken).ConfigureAwait(false);
        var inReview  = await context.Outputs.CountAsync(o => o.UserId == userId && o.Status == OutputStatus.InReview, cancellationToken).ConfigureAwait(false);
        var ready     = await context.Outputs.CountAsync(o => o.UserId == userId && o.Status == OutputStatus.Ready, cancellationToken).ConfigureAwait(false);
        var published = await context.Outputs.CountAsync(o => o.UserId == userId && o.Status == OutputStatus.Published, cancellationToken).ConfigureAwait(false);
        var archived  = await context.Outputs.CountAsync(o => o.UserId == userId && o.Status == OutputStatus.Archived, cancellationToken).ConfigureAwait(false);

        var byType = await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId)
            .GroupBy(o => o.Type)
            .Select(g => new OutputsByTypeDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byArea = await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId)
            .GroupBy(o => new { o.AreaId, AreaName = o.Area != null ? o.Area.Name : null })
            .Select(g => new OutputsByAreaDto(
                g.Key.AreaId,
                g.Key.AreaName ?? "No Area",
                g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new OutputMetricsDto(total, draft, inReview, ready, published, archived, byType, byArea);
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public async Task<OutputDto> CreateAsync(CreateOutputDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Output title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Projects.EnsureOwnedAsync(dto.ProjectId, userId, "Project", cancellationToken)
            .ConfigureAwait(false);
        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);
        await context.Goals.EnsureOwnedAsync(dto.GoalId, userId, "Goal", cancellationToken)
            .ConfigureAwait(false);

        var output = new Output
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            Title       = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Type        = dto.Type,
            Content     = dto.Content,
            Status      = OutputStatus.Draft,
            ProjectId   = dto.ProjectId,
            AreaId      = dto.AreaId,
            GoalId      = dto.GoalId,
            IsAiGenerated = dto.IsAiGenerated,
            Model         = dto.IsAiGenerated ? dto.Model : null,
            PromptVersion = dto.IsAiGenerated ? dto.PromptVersion : null,
        };

        if (dto.SourceNoteIds is not null)
            output.SourceNotes = await ResolveSourceNotesAsync(userId, dto.SourceNoteIds, cancellationToken).ConfigureAwait(false);

        context.Outputs.Add(output);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (projectTitle, areaName, goalTitle) =
            await ResolveLinkedNamesAsync(output, cancellationToken).ConfigureAwait(false);

        return ToDtoWithNames(output, projectTitle, areaName, goalTitle);
    }

    public async Task<OutputDto> UpdateAsync(UpdateOutputDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Output title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Projects.EnsureOwnedAsync(dto.ProjectId, userId, "Project", cancellationToken)
            .ConfigureAwait(false);
        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);
        await context.Goals.EnsureOwnedAsync(dto.GoalId, userId, "Goal", cancellationToken)
            .ConfigureAwait(false);

        var output = await context.Outputs
            .Include(o => o.SourceNotes)
            .FirstOrDefaultAsync(o => o.Id == dto.Id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{dto.Id}' was not found.");

        if (dto.RowVersion is not null)
            context.Entry(output).Property(o => o.RowVersion).OriginalValue = dto.RowVersion;

        // Validate source-note ownership and lifecycle before touching the tracked output.
        // This keeps a rejected update from remaining dirty in the scoped DbContext and
        // being persisted by a later operation on the same service.
        List<Note>? sourceNotes = null;
        if (dto.SourceNoteIds is not null)
            sourceNotes = await ResolveSourceNotesAsync(userId, dto.SourceNoteIds, cancellationToken).ConfigureAwait(false);

        output.Title       = dto.Title.Trim();
        output.Description = dto.Description?.Trim();
        output.Type        = dto.Type;
        output.Status      = dto.Status;
        output.Content     = dto.Content;
        output.ProjectId   = dto.ProjectId;
        output.AreaId      = dto.AreaId;
        output.GoalId      = dto.GoalId;

        if (dto.IsAiGenerated.HasValue)
        {
            output.IsAiGenerated = dto.IsAiGenerated.Value;
            output.Model = dto.IsAiGenerated.Value ? dto.Model : null;
            output.PromptVersion = dto.IsAiGenerated.Value ? dto.PromptVersion : null;
        }

        if (sourceNotes is not null)
        {
            output.SourceNotes.Clear();
            foreach (var note in sourceNotes)
                output.SourceNotes.Add(note);
        }

        // Force one Output UPDATE even when only source-note join rows changed so the
        // rowversion predicate is checked and SQL Server advances the token.
        context.Entry(output).Property(o => o.UpdatedAtUtc).IsModified = true;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("output", ex);
        }

        var (projectTitle, areaName, goalTitle) =
            await ResolveLinkedNamesAsync(output, cancellationToken).ConfigureAwait(false);

        return ToDtoWithNames(output, projectTitle, areaName, goalTitle);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        output.IsArchived   = true;
        output.ArchivedDate = DateTime.UtcNow;
        output.ArchivedReason = normalizedReason;
        output.Status       = OutputStatus.Archived;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(output.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        output.IsArchived   = false;
        output.ArchivedDate = null;
        output.ArchivedReason = null;
        output.Status       = OutputStatus.Draft;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        output.Status        = OutputStatus.Published;
        output.PublishedDate = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        Guid id,
        byte[]? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        if (rowVersion is not null)
            context.Entry(output).Property(o => o.RowVersion).OriginalValue = rowVersion;

        context.Outputs.Remove(output);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("output", ex);
        }
    }

    public async Task AddSourceNoteAsync(Guid outputId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .Include(o => o.SourceNotes)
            .FirstOrDefaultAsync(o => o.Id == outputId && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{outputId}' was not found.");

        if (output.SourceNotes.Any(n => n.Id == noteId))
            return; // already linked — idempotent

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId &&
                                      n.UserId == userId &&
                                      !n.IsArchived &&
                                      n.Status != NoteStatus.Archived,
                cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Active note '{noteId}' was not found.");

        output.SourceNotes.Add(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveSourceNoteAsync(Guid outputId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .Include(o => o.SourceNotes)
            .FirstOrDefaultAsync(o => o.Id == outputId && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{outputId}' was not found.");

        var note = output.SourceNotes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
            return; // not linked — idempotent

        output.SourceNotes.Remove(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<Note>> ResolveSourceNotesAsync(
        string userId,
        IReadOnlyList<Guid> sourceNoteIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = sourceNoteIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return [];

        var notes = await context.Notes
            .Where(note => distinctIds.Contains(note.Id) &&
                           note.UserId == userId &&
                           !note.IsArchived &&
                           note.Status != NoteStatus.Archived)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (notes.Count != distinctIds.Count)
            throw new ArgumentException("One or more source notes are unavailable or archived.", nameof(sourceNoteIds));

        return notes;
    }

    // Projects navigations inline so EF can translate the entire query server-side.
    private static OutputDto ToDto(Output o) => new(
        o.Id,
        o.Title,
        o.Description,
        o.Type,
        o.Status,
        o.IsAiGenerated,
        o.IsArchived,
        o.ProjectId,
        o.Project != null ? o.Project.Name : null,
        o.AreaId,
        o.Area != null ? o.Area.Name : null,
        o.GoalId,
        o.Goal != null ? o.Goal.Title : null,
        o.PublishedDate,
        o.ArchivedDate,
        o.CreatedAtUtc,
        o.UpdatedAtUtc,
        o.RowVersion,
        o.ArchivedReason);

    /// <summary>
    /// Resolves the linked project/area/goal display names for the returned DTO in a
    /// single projection query instead of one round-trip per navigation.
    /// </summary>
    private async Task<(string? ProjectTitle, string? AreaName, string? GoalTitle)> ResolveLinkedNamesAsync(
        Output output, CancellationToken cancellationToken)
    {
        if (output.ProjectId is null && output.AreaId is null && output.GoalId is null)
            return (null, null, null);

        var names = await context.Outputs.AsNoTracking()
            .Where(o => o.Id == output.Id)
            .Select(o => new
            {
                ProjectTitle = o.Project != null ? o.Project.Name : null,
                AreaName     = o.Area != null ? o.Area.Name : null,
                GoalTitle    = o.Goal != null ? o.Goal.Title : null
            })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (names?.ProjectTitle, names?.AreaName, names?.GoalTitle);
    }

    // Used after mutation where navigations are not loaded but names are resolved separately.
    private static OutputDto ToDtoWithNames(Output o, string? projectTitle, string? areaName, string? goalTitle) => new(
        o.Id,
        o.Title,
        o.Description,
        o.Type,
        o.Status,
        o.IsAiGenerated,
        o.IsArchived,
        o.ProjectId,
        projectTitle,
        o.AreaId,
        areaName,
        o.GoalId,
        goalTitle,
        o.PublishedDate,
        o.ArchivedDate,
        o.CreatedAtUtc,
        o.UpdatedAtUtc,
        o.RowVersion,
        o.ArchivedReason);
}
