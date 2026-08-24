using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Brainy.Application.DTOs.DataExport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Produces an explicit, versioned data-portability archive. The export deliberately projects
/// fields instead of serializing tracked entities, which prevents Identity data, ownership ids,
/// row-version tokens, and navigation cycles from leaking into the file.
/// </summary>
internal sealed class DataExportService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IDataExportService
{
    private const string JsonContentType = "application/json;charset=utf-8";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<DataExportFileDto> ExportCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var exportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var areas = await context.Areas.AsNoTracking()
            .Where(area => area.UserId == userId)
            .OrderBy(area => area.Id)
            .Select(area => new
            {
                area.Id,
                area.Name,
                area.Emoji,
                area.Description,
                area.Purpose,
                area.IsArchived,
                area.ArchivedAtUtc,
                area.CreatedAtUtc,
                area.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projects = await context.Projects.AsNoTracking()
            .Where(project => project.UserId == userId)
            .OrderBy(project => project.Id)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Emoji,
                project.Description,
                project.DesiredOutcome,
                project.Status,
                project.Priority,
                project.StartDate,
                project.DueDate,
                project.CompletedDate,
                project.IsArchived,
                project.ArchivedAtUtc,
                project.StatusBeforeArchive,
                project.AreaId,
                project.GoalId,
                project.CreatedAtUtc,
                project.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var resources = await context.Resources.AsNoTracking()
            .Where(resource => resource.UserId == userId)
            .OrderBy(resource => resource.Id)
            .Select(resource => new
            {
                resource.Id,
                resource.Name,
                resource.Emoji,
                resource.Description,
                resource.Topic,
                resource.IsArchived,
                resource.ArchivedAtUtc,
                resource.AreaId,
                resource.CreatedAtUtc,
                resource.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var sources = await context.Sources.AsNoTracking()
            .Where(source => source.UserId == userId)
            .OrderBy(source => source.Id)
            .Select(source => new
            {
                source.Id,
                source.Type,
                source.Title,
                source.Url,
                source.Author,
                source.Reference,
                source.CapturedAtUtc,
                source.CreatedAtUtc,
                source.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var notes = await context.Notes.AsNoTracking()
            .Where(note => note.UserId == userId)
            .OrderBy(note => note.Id)
            .Select(note => new
            {
                note.Id,
                note.Title,
                note.Content,
                note.AiSummary,
                note.Status,
                note.IsArchived,
                note.ArchivedAtUtc,
                note.ProcessedAtUtc,
                note.ParaCategory,
                note.SourceId,
                note.ProjectId,
                note.AreaId,
                note.ResourceId,
                note.IsFavorite,
                note.CreatedAtUtc,
                note.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tags = await context.Tags.AsNoTracking()
            .Where(tag => tag.UserId == userId)
            .OrderBy(tag => tag.Id)
            .Select(tag => new
            {
                tag.Id,
                tag.Name,
                tag.Color,
                tag.CreatedAtUtc,
                tag.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var noteTagRows = await context.Notes.AsNoTracking()
            .Where(note => note.UserId == userId)
            .OrderBy(note => note.Id)
            .Select(note => new
            {
                NoteId = note.Id,
                TagIds = note.Tags.Where(tag => tag.UserId == userId).Select(tag => tag.Id).OrderBy(id => id).ToList()
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var noteTagLinks = noteTagRows
            .SelectMany(row => row.TagIds.Select(tagId => new { row.NoteId, TagId = tagId }))
            .ToList();

        var resourceTagRows = await context.Resources.AsNoTracking()
            .Where(resource => resource.UserId == userId)
            .OrderBy(resource => resource.Id)
            .Select(resource => new
            {
                ResourceId = resource.Id,
                TagIds = resource.Tags.Where(tag => tag.UserId == userId).Select(tag => tag.Id).OrderBy(id => id).ToList()
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var resourceTagLinks = resourceTagRows
            .SelectMany(row => row.TagIds.Select(tagId => new { row.ResourceId, TagId = tagId }))
            .ToList();

        var imageRows = await context.NoteImages.AsNoTracking()
            .Where(image => image.UserId == userId)
            .OrderBy(image => image.Id)
            .Select(image => new
            {
                image.Id,
                image.NoteId,
                image.FileName,
                image.ContentType,
                image.SizeBytes,
                image.Data,
                image.CreatedAtUtc,
                image.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var noteImages = imageRows.Select(image => new
        {
            image.Id,
            image.NoteId,
            FileName = Path.GetFileName(image.FileName),
            image.ContentType,
            image.SizeBytes,
            Sha256 = Convert.ToHexString(SHA256.HashData(image.Data)).ToLowerInvariant(),
            DataBase64 = Convert.ToBase64String(image.Data),
            image.CreatedAtUtc,
            image.UpdatedAtUtc
        }).ToList();

        var highlights = await context.Highlights.AsNoTracking()
            .Where(highlight => highlight.Note.UserId == userId)
            .OrderBy(highlight => highlight.Id)
            .Select(highlight => new
            {
                highlight.Id,
                highlight.NoteId,
                highlight.Text,
                highlight.Annotation,
                highlight.Layer,
                highlight.StartOffset,
                highlight.EndOffset,
                highlight.CreatedAtUtc,
                highlight.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var summaries = await context.Summaries.AsNoTracking()
            .Where(summary => summary.Note.UserId == userId)
            .OrderBy(summary => summary.Id)
            .Select(summary => new
            {
                summary.Id,
                summary.NoteId,
                summary.Content,
                summary.IsAiGenerated,
                summary.Model,
                summary.PromptVersion,
                summary.CreatedAtUtc,
                summary.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var actionItems = await context.ActionItems.AsNoTracking()
            .Where(action => action.UserId == userId)
            .OrderBy(action => action.Id)
            .Select(action => new
            {
                action.Id,
                action.Title,
                action.Description,
                action.Status,
                action.IsAiGenerated,
                action.Model,
                action.PromptVersion,
                action.NoteId,
                action.TaskItemId,
                action.CreatedAtUtc,
                action.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var noteRelationships = await context.NoteRelationships.AsNoTracking()
            .Where(relationship =>
                relationship.SourceNote.UserId == userId &&
                relationship.TargetNote.UserId == userId)
            .OrderBy(relationship => relationship.Id)
            .Select(relationship => new
            {
                relationship.Id,
                relationship.SourceNoteId,
                relationship.TargetNoteId,
                relationship.Type,
                relationship.Annotation,
                relationship.IsAiGenerated,
                relationship.CreatedAtUtc,
                relationship.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasks = await context.Tasks.AsNoTracking()
            .Where(task => task.UserId == userId)
            .OrderBy(task => task.Id)
            .Select(task => new
            {
                task.Id,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueDate,
                task.CompletedDate,
                task.IsArchived,
                task.IsCurrentTask,
                task.SortOrder,
                task.ArchivedAtUtc,
                task.ProjectId,
                task.Complexity,
                task.ParentTaskId,
                task.IsRecurring,
                task.RecurrenceType,
                task.RecurrenceInterval,
                task.RecurrenceEndDate,
                task.NextOccurrenceDate,
                task.RecurrenceSourceTaskId,
                task.CreatedAtUtc,
                task.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var taskDependencies = await context.TaskDependencies.AsNoTracking()
            .Where(dependency =>
                dependency.Task.UserId == userId &&
                dependency.DependsOnTask.UserId == userId)
            .OrderBy(dependency => dependency.Id)
            .Select(dependency => new
            {
                dependency.Id,
                dependency.TaskId,
                dependency.DependsOnTaskId,
                dependency.CreatedAtUtc,
                dependency.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputs = await context.Outputs.AsNoTracking()
            .Where(output => output.UserId == userId)
            .OrderBy(output => output.Id)
            .Select(output => new
            {
                output.Id,
                output.Title,
                output.Description,
                output.Content,
                output.Type,
                output.Status,
                output.IsAiGenerated,
                output.Model,
                output.PromptVersion,
                output.ProjectId,
                output.AreaId,
                output.GoalId,
                output.PublishedDate,
                output.ArchivedDate,
                output.IsArchived,
                output.CreatedAtUtc,
                output.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputSourceRows = await context.Outputs.AsNoTracking()
            .Where(output => output.UserId == userId)
            .OrderBy(output => output.Id)
            .Select(output => new
            {
                OutputId = output.Id,
                NoteIds = output.SourceNotes
                    .Where(note => note.UserId == userId)
                    .Select(note => note.Id)
                    .OrderBy(id => id)
                    .ToList()
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var outputSourceNoteLinks = outputSourceRows
            .SelectMany(row => row.NoteIds.Select(noteId => new { row.OutputId, NoteId = noteId }))
            .ToList();

        var ideas = await context.Ideas.AsNoTracking()
            .Where(idea => idea.UserId == userId)
            .OrderBy(idea => idea.Id)
            .Select(idea => new
            {
                idea.Id,
                idea.Title,
                idea.Description,
                idea.AreaId,
                idea.Priority,
                idea.Status,
                idea.IsArchived,
                idea.ArchivedAtUtc,
                idea.Research,
                idea.Competitors,
                idea.Notes,
                idea.TargetUserAndProblem,
                idea.SuitabilityReason,
                idea.Evidence,
                idea.ValidationExperiment,
                idea.ReplacedCommitment,
                idea.CommittedProjectId,
                idea.CommittedAtUtc,
                idea.CreatedAtUtc,
                idea.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goals = await context.Goals.AsNoTracking()
            .Where(goal => goal.UserId == userId)
            .OrderBy(goal => goal.Id)
            .Select(goal => new
            {
                goal.Id,
                goal.Title,
                goal.Description,
                goal.Status,
                goal.TargetDate,
                goal.AchievedDate,
                goal.IsArchived,
                goal.ArchivedAtUtc,
                goal.AreaId,
                goal.CreatedAtUtc,
                goal.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goalMilestones = await context.GoalMilestones.AsNoTracking()
            .Where(milestone => milestone.Goal != null && milestone.Goal.UserId == userId)
            .OrderBy(milestone => milestone.Id)
            .Select(milestone => new
            {
                milestone.Id,
                milestone.GoalId,
                milestone.Title,
                milestone.IsCompleted,
                milestone.CompletedAtUtc,
                milestone.CreatedAtUtc,
                milestone.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goalActivities = await context.GoalActivities.AsNoTracking()
            .Where(activity => activity.Goal.UserId == userId)
            .OrderBy(activity => activity.Id)
            .Select(activity => new
            {
                activity.Id,
                activity.GoalId,
                activity.ActivityType,
                activity.Description,
                activity.OldValue,
                activity.NewValue,
                activity.CreatedAtUtc,
                activity.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var archiveRetentionRules = await context.ArchiveRetentionRules.AsNoTracking()
            .Where(rule => rule.UserId == userId)
            .OrderBy(rule => rule.Id)
            .Select(rule => new
            {
                rule.Id,
                rule.EntityType,
                rule.RetentionDays,
                rule.CreatedAtUtc,
                rule.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var dashboardPreferences = await context.DashboardPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .OrderBy(preference => preference.Id)
            .Select(preference => new
            {
                preference.Id,
                preference.WidgetOrder,
                preference.CollapsedWidgets,
                preference.InboxWarningThreshold,
                preference.TimeZoneId,
                preference.CreatedAtUtc,
                preference.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var lifecycleActivities = await context.LifecycleActivities.AsNoTracking()
            .Where(activity => activity.UserId == userId)
            .OrderBy(activity => activity.OccurredAtUtc)
            .ThenBy(activity => activity.Id)
            .Select(activity => new
            {
                activity.Id,
                activity.EntityId,
                activity.ActivityType,
                activity.OccurredAtUtc,
                activity.Title,
                activity.Context,
                activity.Link
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var weeklyTaskSelections = await context.WeeklyTaskSelections.AsNoTracking()
            .Where(selection => selection.UserId == userId)
            .OrderBy(selection => selection.WeekStartDate)
            .ThenBy(selection => selection.TaskId)
            .Select(selection => new
            {
                selection.Id,
                selection.TaskId,
                selection.WeekStartDate,
                selection.CreatedAtUtc,
                selection.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var archive = new
        {
            SchemaVersion = IDataExportService.SchemaVersion,
            Product = "Brainy",
            ExportedAtUtc = exportedAtUtc,
            Security = new
            {
                AccountCredentialsIncluded = false,
                ApplicationSecretsIncluded = false,
                Note = "User-authored content is exported verbatim; secrets saved in notes or source URLs remain in that content."
            },
            Images = new
            {
                Encoding = "base64",
                Integrity = "sha256",
                Note = "Image bytes are inert base64 strings. Verify sha256 before restoring."
            },
            Data = new
            {
                Areas = areas,
                Projects = projects,
                Resources = resources,
                Sources = sources,
                Notes = notes,
                Tags = tags,
                NoteTagLinks = noteTagLinks,
                ResourceTagLinks = resourceTagLinks,
                NoteImages = noteImages,
                Highlights = highlights,
                Summaries = summaries,
                ActionItems = actionItems,
                NoteRelationships = noteRelationships,
                Tasks = tasks,
                TaskDependencies = taskDependencies,
                Outputs = outputs,
                OutputSourceNoteLinks = outputSourceNoteLinks,
                Ideas = ideas,
                Goals = goals,
                GoalMilestones = goalMilestones,
                GoalActivities = goalActivities,
                ArchiveRetentionRules = archiveRetentionRules,
                DashboardPreferences = dashboardPreferences,
                LifecycleActivities = lifecycleActivities,
                WeeklyTaskSelections = weeklyTaskSelections
            }
        };

        var content = JsonSerializer.SerializeToUtf8Bytes(archive, SerializerOptions);
        var fileName = $"brainy-data-export-{exportedAtUtc:yyyyMMdd-HHmmss}Z-v{IDataExportService.SchemaVersion}.json";

        return new DataExportFileDto(fileName, JsonContentType, IDataExportService.SchemaVersion, content);
    }
}
