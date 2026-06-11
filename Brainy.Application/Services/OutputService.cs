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
            sourceNotes);
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
            GoalId      = dto.GoalId
        };

        context.Outputs.Add(output);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Resolve navigation names for the returned DTO.
        string? projectTitle = null;
        string? areaName     = null;
        string? goalTitle    = null;

        if (output.ProjectId.HasValue)
            projectTitle = await context.Projects.AsNoTracking()
                .Where(p => p.Id == output.ProjectId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (output.AreaId.HasValue)
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == output.AreaId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (output.GoalId.HasValue)
            goalTitle = await context.Goals.AsNoTracking()
                .Where(g => g.Id == output.GoalId.Value)
                .Select(g => g.Title)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return ToDtoWithNames(output, projectTitle, areaName, goalTitle);
    }

    public async Task<OutputDto> UpdateAsync(UpdateOutputDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Output title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == dto.Id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{dto.Id}' was not found.");

        output.Title       = dto.Title.Trim();
        output.Description = dto.Description?.Trim();
        output.Type        = dto.Type;
        output.Status      = dto.Status;
        output.Content     = dto.Content;
        output.ProjectId   = dto.ProjectId;
        output.AreaId      = dto.AreaId;
        output.GoalId      = dto.GoalId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? projectTitle = null;
        string? areaName     = null;
        string? goalTitle    = null;

        if (output.ProjectId.HasValue)
            projectTitle = await context.Projects.AsNoTracking()
                .Where(p => p.Id == output.ProjectId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (output.AreaId.HasValue)
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == output.AreaId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (output.GoalId.HasValue)
            goalTitle = await context.Goals.AsNoTracking()
                .Where(g => g.Id == output.GoalId.Value)
                .Select(g => g.Title)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return ToDtoWithNames(output, projectTitle, areaName, goalTitle);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        output.IsArchived   = true;
        output.ArchivedDate = DateTime.UtcNow;
        output.Status       = OutputStatus.Archived;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        output.IsArchived   = false;
        output.ArchivedDate = null;
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

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{id}' was not found.");

        context.Outputs.Remove(output);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

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
        o.UpdatedAtUtc);

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
        o.UpdatedAtUtc);
}
