using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Outputs;
using Brainy.Application.DTOs.Templates;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Services.Templates;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD for <see cref="OutputTemplate"/> blueprints and instantiates them into
/// real outputs through <see cref="IOutputService"/>.
/// </summary>
internal sealed class OutputTemplateService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache,
    IOutputService outputService) : IOutputTemplateService
{
    public async Task<IReadOnlyList<OutputTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await SeedBuiltInIfEmptyAsync(userId, cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "output-templates:all",
            ReadTags(),
            async ct =>
            {
                var templates = await Query(userId).OrderBy(t => t.Name).ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlyList<OutputTemplateDto>)templates.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"output-templates:{id}",
            ReadTags(id),
            async ct =>
            {
                var template = await Query(userId).FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
                return template is null ? null : ToDto(template);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputTemplateDto> CreateAsync(CreateOutputTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TitlePattern))
            throw new ArgumentException("Output title pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = new OutputTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Trim(),
            TitlePattern = dto.TitlePattern.Trim(),
            Type = dto.Type,
            ContentScaffold = dto.ContentScaffold,
            DefaultSourceSelection = dto.DefaultSourceSelection,
            IsBuiltIn = false
        };

        context.OutputTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<OutputTemplateDto> UpdateAsync(UpdateOutputTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Template name is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TitlePattern))
            throw new ArgumentException("Output title pattern is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.OutputTemplates
            .FirstOrDefaultAsync(t => t.Id == dto.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output template '{dto.Id}' was not found.");

        if (dto.RowVersion is not null)
            context.Entry(template).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        template.Name = dto.Name.Trim();
        template.TitlePattern = dto.TitlePattern.Trim();
        template.Type = dto.Type;
        template.ContentScaffold = dto.ContentScaffold;
        template.DefaultSourceSelection = dto.DefaultSourceSelection;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("output template", ex);
        }

        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.OutputTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output template '{id}' was not found.");

        context.OutputTemplates.Remove(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);
    }

    public async Task<OutputTemplateDto> CreateFromOutputAsync(SaveOutputAsTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.TemplateName))
            throw new ArgumentException("Template name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var output = await context.Outputs
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == dto.OutputId && o.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output '{dto.OutputId}' was not found.");

        var template = new OutputTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.TemplateName.Trim(),
            TitlePattern = output.Title,
            Type = output.Type,
            ContentScaffold = output.Content,
            DefaultSourceSelection = OutputTemplateSourceSelectionMode.None,
            IsBuiltIn = false
        };

        context.OutputTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(userId, template.Id).ConfigureAwait(false);

        return ToDto(template);
    }

    public async Task<OutputDto> InstantiateAsync(InstantiateOutputTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        var template = await context.OutputTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == dto.TemplateId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Output template '{dto.TemplateId}' was not found.");

        var title = string.IsNullOrWhiteSpace(dto.Title)
            ? TemplatePatternResolver.Resolve(template.TitlePattern, today)
            : dto.Title.Trim();

        IReadOnlyList<Guid>? sourceNoteIds = null;
        if (template.DefaultSourceSelection == OutputTemplateSourceSelectionMode.ActiveProjectNotes && dto.ProjectId.HasValue)
        {
            sourceNoteIds = await context.Notes
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.ProjectId == dto.ProjectId.Value && !n.IsArchived)
                .Select(n => n.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await outputService.CreateAsync(new CreateOutputDto(
            Title: title,
            Description: null,
            Type: template.Type,
            Content: template.ContentScaffold,
            ProjectId: dto.ProjectId,
            AreaId: dto.AreaId,
            GoalId: dto.GoalId,
            SourceNoteIds: sourceNoteIds), cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedBuiltInIfEmptyAsync(string userId, CancellationToken cancellationToken)
    {
        var hasAny = await context.OutputTemplates.AsNoTracking()
            .AnyAsync(t => t.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (hasAny) return;

        foreach (var template in BuiltInTemplateSet.BuildOutputTemplates(userId))
            context.OutputTemplates.Add(template);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<OutputTemplate> Query(string userId) =>
        context.OutputTemplates.AsNoTracking().Where(t => t.UserId == userId);

    private static OutputTemplateDto ToDto(OutputTemplate t) => new(
        t.Id, t.Name, t.TitlePattern, t.Type, t.ContentScaffold, t.DefaultSourceSelection,
        t.IsBuiltIn, t.CreatedAtUtc, t.UpdatedAtUtc, t.RowVersion);

    private static IReadOnlyCollection<string> ReadTags(Guid? templateId = null)
    {
        List<string> tags = [ApplicationCacheKey.EntityTypeTag<OutputTemplate>()];
        if (templateId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<OutputTemplate>(templateId.Value));
        return tags;
    }

    private ValueTask InvalidateAsync(string userId, Guid templateId) =>
        cache.InvalidateTagsAsync(
            userId,
            [ApplicationCacheKey.EntityTypeTag<OutputTemplate>(), ApplicationCacheKey.EntityTag<OutputTemplate>(templateId)],
            CancellationToken.None);
}
