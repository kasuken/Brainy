using System.Text.Json;
using Brainy.Application.Common;
using Brainy.Application.DTOs.ActionItems;
using Brainy.Application.Interfaces.AI;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>Implements the CODE Distill workflow for note action items.</summary>
internal sealed class ActionItemService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IAiAssistant aiAssistant) : IActionItemService
{
    private const int MaxTitleLength = 500;
    private const int MaxDescriptionLength = 2000;
    private const int MaxAiActionsPerExtraction = 50;

    public async Task<IReadOnlyList<ActionItemDto>> GetByNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var noteExists = await context.Notes.AsNoTracking()
            .AnyAsync(note => note.Id == noteId && note.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!noteExists)
            throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        return await QueryDtos(userId)
            .Where(action => action.NoteId == noteId)
            .OrderBy(action => action.Status == ActionItemStatus.Done)
            .ThenBy(action => action.Status == ActionItemStatus.Dismissed)
            .ThenByDescending(action => action.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ActionItemDto> CreateAsync(
        CreateActionItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var title = ValidateAndNormalizeTitle(dto.Title);
        var description = NormalizeDescription(dto.Description);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await EnsureOwnedNoteAsync(dto.NoteId, userId, cancellationToken).ConfigureAwait(false);

        var action = new ActionItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NoteId = dto.NoteId,
            Title = title,
            Description = description,
            Status = ActionItemStatus.Open,
        };

        context.ActionItems.Add(action);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(action);
    }

    public async Task<ActionItemDto> UpdateAsync(
        UpdateActionItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var title = ValidateAndNormalizeTitle(dto.Title);
        var description = NormalizeDescription(dto.Description);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var action = await context.ActionItems
            .FirstOrDefaultAsync(item => item.Id == dto.Id && item.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Action item '{dto.Id}' was not found.");

        if (dto.RowVersion is not null)
            context.Entry(action).Property(item => item.RowVersion).OriginalValue = dto.RowVersion;

        action.Title = title;
        action.Description = description;
        action.Status = dto.Status;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("action item", ex);
        }

        return await GetDtoAsync(action.Id, userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var action = await context.ActionItems
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Action item '{id}' was not found.");

        context.ActionItems.Remove(action);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActionItemDto>> ExtractFromNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes.AsNoTracking()
            .Where(item => item.Id == noteId && item.UserId == userId)
            .Select(item => new { item.Id, item.Content })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        if (string.IsNullOrWhiteSpace(note.Content))
            throw new InvalidOperationException("Add note content before extracting actions.");

        var result = await aiAssistant.ExtractActionItemsAsync(note.Content, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "AI action extraction failed.");

        var extractedTitles = ParseExtractedTitles(result.Content);
        if (extractedTitles.Count == 0)
            return [];

        var existingTitles = await context.ActionItems.AsNoTracking()
            .Where(item => item.UserId == userId && item.NoteId == noteId)
            .Select(item => item.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var knownTitles = new HashSet<string>(existingTitles, StringComparer.OrdinalIgnoreCase);

        var actions = extractedTitles
            .Where(knownTitles.Add)
            .Select(title => new ActionItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NoteId = noteId,
                Title = title,
                Status = ActionItemStatus.Open,
                IsAiGenerated = true,
                Model = result.Model,
                PromptVersion = result.PromptVersion,
            })
            .ToList();

        if (actions.Count == 0)
            return [];

        context.ActionItems.AddRange(actions);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return actions.Select(action => ToDto(action)).ToList();
    }

    public async Task<ActionItemDto> PromoteToTaskAsync(
        Guid id,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var action = await context.ActionItems
            .Include(item => item.TaskItem)
            .ThenInclude(task => task!.Project)
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Action item '{id}' was not found.");

        if (action.TaskItem is not null)
        {
            if (action.TaskItem.UserId != userId)
                throw new InvalidOperationException("The promoted task is not owned by the current user.");
            if (action.TaskItem.ProjectId != projectId)
                throw new InvalidOperationException(
                    $"This action is already promoted to project '{action.TaskItem.Project.Name}'.");

            return ToDto(action, action.TaskItem.ProjectId, action.TaskItem.Project.Name);
        }

        var project = await context.Projects
            .FirstOrDefaultAsync(item => item.Id == projectId && item.UserId == userId &&
                                         !item.IsArchived && item.Status == ProjectStatus.Active,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Active project '{projectId}' was not found.");

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = project.Id,
            Title = action.Title,
            Description = action.Description,
            Status = TaskItemStatus.Todo,
            Priority = TaskPriority.Medium,
        };

        context.Tasks.Add(task);
        action.TaskItemId = task.Id;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("action item promotion", ex);
        }

        return ToDto(action, project.Id, project.Name);
    }

    private IQueryable<ActionItemDto> QueryDtos(string userId) =>
        context.ActionItems.AsNoTracking()
            .Where(action => action.UserId == userId && action.NoteId.HasValue)
            .Select(action => new ActionItemDto(
                action.Id,
                action.NoteId!.Value,
                action.Title,
                action.Description,
                action.Status,
                action.IsAiGenerated,
                action.Model,
                action.PromptVersion,
                action.TaskItemId,
                action.TaskItem != null ? action.TaskItem.ProjectId : null,
                action.TaskItem != null ? action.TaskItem.Project.Name : null,
                action.CreatedAtUtc,
                action.UpdatedAtUtc,
                action.RowVersion));

    private async Task<ActionItemDto> GetDtoAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken) =>
        await QueryDtos(userId)
            .FirstAsync(action => action.Id == id, cancellationToken)
            .ConfigureAwait(false);

    private async Task EnsureOwnedNoteAsync(
        Guid noteId,
        string userId,
        CancellationToken cancellationToken)
    {
        var exists = await context.Notes.AsNoTracking()
            .AnyAsync(note => note.Id == noteId && note.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            throw new KeyNotFoundException($"Note '{noteId}' was not found.");
    }

    private static IReadOnlyList<string> ParseExtractedTitles(string content)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(content);
            if (parsed is null)
                throw new InvalidOperationException("AI returned an invalid action-item response.");

            var titles = parsed
                .Select(ValidateAndNormalizeTitle)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxAiActionsPerExtraction)
                .ToList();
            return titles;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("AI returned an invalid action-item response.", ex);
        }
    }

    private static string ValidateAndNormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Action title is required.", nameof(value));

        var title = value.Trim();
        if (title.Length > MaxTitleLength)
            throw new ArgumentException($"Action title cannot exceed {MaxTitleLength} characters.", nameof(value));
        return title;
    }

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var description = value.Trim();
        if (description.Length > MaxDescriptionLength)
            throw new ArgumentException(
                $"Action description cannot exceed {MaxDescriptionLength} characters.", nameof(value));
        return description;
    }

    private static ActionItemDto ToDto(ActionItem action, Guid? projectId = null, string? projectName = null) =>
        new(
            action.Id,
            action.NoteId ?? throw new InvalidOperationException("Action item is not linked to a note."),
            action.Title,
            action.Description,
            action.Status,
            action.IsAiGenerated,
            action.Model,
            action.PromptVersion,
            action.TaskItemId,
            projectId,
            projectName,
            action.CreatedAtUtc,
            action.UpdatedAtUtc,
            action.RowVersion);
}
