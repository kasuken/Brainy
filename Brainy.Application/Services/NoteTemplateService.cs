using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.DTOs.Templates;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Services.Templates;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD for <see cref="NoteTemplate"/> blueprints and instantiates them into
/// real notes through <see cref="INoteService"/>, so an initial note revision is still
/// recorded exactly as it would be for a hand-made note.
/// </summary>
internal sealed class NoteTemplateService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache,
    INoteService noteService) : INoteTemplateService
{
    public async Task<IReadOnlyList<NoteTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await SeedBuiltInIfEmptyAsync(userId, cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "note-templates:all",
            ReadTags(),
            async ct =>
            {
                var templates = await Query(userId).OrderBy(t => t.Name).ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlyList<NoteTemplateDto>)templates.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"note-templates:{id}",
            ReadTags(id),
            async ct =>
            {
                var template = await Query(userId).FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
                return template is null ? null : ToDto(template);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteTemplateDto> CreateAsync(CreateNoteTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TitlePattern))
            throw new ArgumentException("Note title pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = new NoteTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Trim(),
            TitlePattern = dto.TitlePattern.Trim(),
            ContentScaffold = dto.ContentScaffold,
            DefaultParaCategory = dto.DefaultParaCategory,
            IsBuiltIn = false
        };

        context.NoteTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<NoteTemplateDto> UpdateAsync(UpdateNoteTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TitlePattern))
            throw new ArgumentException("Note title pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.NoteTemplates
            .FirstOrDefaultAsync(t => t.Id == dto.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note template '{dto.Id}' was not found.");

        if (dto.RowVersion is not null)
            context.Entry(template).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        template.Name = dto.Name.Trim();
        template.TitlePattern = dto.TitlePattern.Trim();
        template.ContentScaffold = dto.ContentScaffold;
        template.DefaultParaCategory = dto.DefaultParaCategory;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("note template", ex);
        }

        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.NoteTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note template '{id}' was not found.");

        context.NoteTemplates.Remove(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
    }

    public async Task<NoteTemplateDto> CreateFromNoteAsync(SaveNoteAsTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.TemplateName))
            throw new ArgumentException("Template name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == dto.NoteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.NoteId}' was not found.");

        var template = new NoteTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.TemplateName.Trim(),
            TitlePattern = note.Title,
            ContentScaffold = note.Content,
            DefaultParaCategory = note.ParaCategory,
            IsBuiltIn = false
        };

        context.NoteTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<NoteDto> InstantiateAsync(InstantiateNoteTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.NoteTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == dto.TemplateId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note template '{dto.TemplateId}' was not found.");

        var title = string.IsNullOrWhiteSpace(dto.Title)
            ? TemplatePatternResolver.Resolve(template.TitlePattern, today)
            : dto.Title.Trim();

        return await noteService.CreateAsync(new CreateNoteDto(
            Title: title,
            Content: template.ContentScaffold,
            ParaCategory: template.DefaultParaCategory,
            ProjectId: dto.ProjectId,
            AreaId: dto.AreaId,
            ResourceId: dto.ResourceId), cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedBuiltInIfEmptyAsync(string userId, CancellationToken cancellationToken)
    {
        var hasAny = await context.NoteTemplates.AsNoTracking()
            .AnyAsync(t => t.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (hasAny) return;

        foreach (var template in BuiltInTemplateSet.BuildNoteTemplates(userId))
            context.NoteTemplates.Add(template);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<NoteTemplate> Query(string userId) =>
        context.NoteTemplates.AsNoTracking().Where(t => t.UserId == userId);

    private static NoteTemplateDto ToDto(NoteTemplate t) => new(
        t.Id, t.Name, t.TitlePattern, t.ContentScaffold, t.DefaultParaCategory,
        t.IsBuiltIn, t.CreatedAtUtc, t.UpdatedAtUtc, t.RowVersion);

    private static IReadOnlyCollection<string> ReadTags(Guid? templateId = null)
    {
        List<string> tags = [ApplicationCacheKey.EntityTypeTag<NoteTemplate>()];
        if (templateId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<NoteTemplate>(templateId.Value));
        return tags;
    }

    private ValueTask InvalidateAsync(string userId, Guid templateId) =>
        cache.InvalidateTagsAsync(
            userId,
            [ApplicationCacheKey.EntityTypeTag<NoteTemplate>(), ApplicationCacheKey.EntityTag<NoteTemplate>(templateId)],
            CancellationToken.None);
}
