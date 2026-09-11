using System.Security.Cryptography;
using System.Text.Json;
using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Common;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Restores a versioned Brainy export for the current user. Every content entity the
/// export contains is imported in dependency order (areas/sources first, notes and
/// tasks in the middle, link tables last), with cross-references remapped from the
/// export's original ids to freshly generated ones.
///
/// Three sections are deliberately never imported and are always reported as
/// unsupported instead:
/// - <c>lifecycleActivities</c>: an append-only audit ledger of what happened in the
///   *old* account. Replaying it would misrepresent history in the new account, and
///   importing notes/tasks/projects/outputs/ideas/goals already appends fresh,
///   accurate lifecycle entries for the moment they enter this account (see
///   <c>BrainyDbContext.AppendLifecycleActivities</c>).
/// - <c>productEvents</c>: internal analytics events scoped to the old account's
///   usage timeline; replaying them would corrupt activation/retention measurement.
/// - <c>dashboardPreferences</c>: per-device UI/consent settings (widget layout,
///   analytics opt-in, Starter Mode, onboarding progress). The importing account
///   already has its own preference row; overwriting it from an unrelated export
///   would silently clobber decisions the current user already made.
///
/// Audit timestamps (<c>createdAtUtc</c>/<c>updatedAtUtc</c>) are never restored from
/// the export: <c>BrainyDbContext.ApplyAuditTimestamps</c> always stamps newly added
/// rows with the moment they enter this account, which this import treats as correct
/// — those columns describe this account's history, not portable content.
///
/// Duplicate-safety: a second import of the same export does not duplicate data.
/// Top-level content rows (Area, Source, Goal, Project, Resource, Note, Task,
/// Output, Idea) are matched against existing rows for the user by a content-based
/// natural key (name/title, not the export's id, since a fresh id is always minted
/// on create) and reused rather than recreated when found. Child rows with a plain
/// scalar foreign key (Highlights, Summaries, Note images, Action items, Goal
/// milestones/activities, Note relationships, Task dependencies, Weekly task
/// selections) are individually deduplicated against existing rows, so they are
/// still created for a reused parent if genuinely new. The two many-to-many link
/// sections that ride on shadow join tables (Note/Resource tag links) are a known,
/// narrower exception: they are only established for newly created notes/resources
/// in this run, not re-synced onto a reused note/resource — a smaller, recoverable
/// gap (a missing tag) rather than a duplicated content row.
/// </summary>
internal sealed class DataImportService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IDataImportService
{
    private static readonly HashSet<string> SupportedSections =
    [
        "areas", "sources", "goals", "projects", "resources", "tags",
        "notes", "noteTagLinks", "resourceTagLinks",
        "noteImages", "highlights", "summaries",
        "tasks", "actionItems", "noteRelationships", "taskDependencies",
        "outputs", "outputSourceNoteLinks",
        "ideas", "goalMilestones", "goalActivities",
        "archiveRetentionRules", "weeklyTaskSelections"
    ];

    private static readonly Dictionary<string, string> SectionLabels = new(StringComparer.Ordinal)
    {
        ["areas"] = "Areas",
        ["projects"] = "Projects",
        ["resources"] = "Resources",
        ["sources"] = "Sources",
        ["noteImages"] = "Note images",
        ["highlights"] = "Highlights",
        ["summaries"] = "Summaries",
        ["actionItems"] = "Action items",
        ["noteRelationships"] = "Note relationships",
        ["tasks"] = "Tasks",
        ["taskDependencies"] = "Task dependencies",
        ["outputs"] = "Outputs",
        ["outputSourceNoteLinks"] = "Output source note links",
        ["ideas"] = "Ideas",
        ["goals"] = "Goals",
        ["goalMilestones"] = "Goal milestones",
        ["goalActivities"] = "Goal activities",
        ["archiveRetentionRules"] = "Archive retention rules",
        ["dashboardPreferences"] = "Dashboard preferences",
        ["lifecycleActivities"] = "Lifecycle activities",
        ["weeklyTaskSelections"] = "Weekly task selections",
        ["productEvents"] = "Analytics events"
    };

    public async Task<DataImportPreviewDto> PreviewImportAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateRoot(root);
        var schemaVersion = ValidateSchemaVersion(root);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var data = root.GetProperty("data");

        var state = new ImportState(userId, commit: false);
        await RunPlanAsync(data, state, cancellationToken).ConfigureAwait(false);

        return new DataImportPreviewDto(
            schemaVersion, state.BuildOutcomes(), ComputeUnsupported(data), state.Conflicts, state.IntegrityIssues);
    }

    public async Task<DataImportResultDto> ImportCurrentUserAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateRoot(root);
        var schemaVersion = ValidateSchemaVersion(root);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var data = root.GetProperty("data");

        var state = await context.ExecuteInTransactionAsync(async ct =>
        {
            var importState = new ImportState(userId, commit: true);
            await RunPlanAsync(data, importState, ct).ConfigureAwait(false);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return importState;
        }, cancellationToken).ConfigureAwait(false);

        await cache.InvalidateUserAsync(userId, CancellationToken.None).ConfigureAwait(false);

        return new DataImportResultDto(
            schemaVersion, state.BuildOutcomes(), ComputeUnsupported(data), state.Conflicts, state.IntegrityIssues);
    }

    private async Task RunPlanAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        await ImportAreasAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportSourcesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportGoalsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportProjectsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportResourcesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportTagsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportNotesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportResourceTagLinksAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportNoteImagesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportHighlightsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportSummariesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportTasksAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportActionItemsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportNoteRelationshipsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportTaskDependenciesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportOutputsAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportOutputSourceNoteLinksAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportIdeasAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportGoalMilestonesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportGoalActivitiesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportArchiveRetentionRulesAsync(data, state, cancellationToken).ConfigureAwait(false);
        await ImportWeeklyTaskSelectionsAsync(data, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportAreasAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "areas");
        var existing = await context.Areas.AsNoTracking()
            .Where(area => area.UserId == state.UserId)
            .Select(area => new { area.Id, area.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByName = BuildNameLookup(existing.Select(a => (a.Name, a.Id)), "Area", state.Conflicts);

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var name = ReadRequiredString(row, "name", 200);
            if (name is null)
            {
                skipped++;
                continue;
            }

            if (existingByName.TryGetValue(NormalizeKey(name), out var existingId))
            {
                state.AreaIds[exportId] = existingId;
                reused++;
                continue;
            }

            var area = new Area
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Name = name,
                Emoji = ReadOptionalString(row, "emoji", 16) ?? AreaEmojiDefaults.DefaultEmoji,
                Description = ReadOptionalString(row, "description", 2000),
                Purpose = ReadOptionalString(row, "purpose", 2000),
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc")
            };

            if (state.Commit) context.Areas.Add(area);
            state.AreaIds[exportId] = area.Id;
            existingByName[NormalizeKey(name)] = area.Id;
            created++;
        }

        state.RecordOutcome("Areas", created, reused, skipped);
    }

    private async Task ImportSourcesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "sources");
        var existing = await context.Sources.AsNoTracking()
            .Where(source => source.UserId == state.UserId)
            .Select(source => new { source.Id, source.Type, source.Title, source.Url })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var source in existing)
            existingByKey[SourceKey(source.Type, source.Title, source.Url)] = source.Id;

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId) || !TryReadEnum(row, "type", out SourceType type))
            {
                skipped++;
                continue;
            }

            var title = ReadOptionalString(row, "title", 500);
            var url = ReadOptionalString(row, "url", 2048);
            var key = SourceKey(type, title, url);

            if (existingByKey.TryGetValue(key, out var existingId))
            {
                state.SourceIds[exportId] = existingId;
                reused++;
                continue;
            }

            var source = new Source
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Type = type,
                Title = title,
                Url = url,
                Author = ReadOptionalString(row, "author", 200),
                Reference = ReadOptionalString(row, "reference", 1000),
                CapturedAtUtc = ReadNullableDateTime(row, "capturedAtUtc")
            };

            if (state.Commit) context.Sources.Add(source);
            state.SourceIds[exportId] = source.Id;
            existingByKey[key] = source.Id;
            created++;
        }

        state.RecordOutcome("Sources", created, reused, skipped);
    }

    private static string SourceKey(SourceType type, string? title, string? url) =>
        $"{type}|{title?.Trim().ToLowerInvariant()}|{url?.Trim().ToLowerInvariant()}";

    private async Task ImportGoalsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "goals");
        var existing = await context.Goals.AsNoTracking()
            .Where(goal => goal.UserId == state.UserId)
            .Select(goal => new { goal.Id, goal.Title })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByTitle = BuildNameLookup(existing.Select(g => (g.Title, g.Id)), "Goal", state.Conflicts);

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var title = ReadRequiredString(row, "title", 200);
            if (title is null ||
                !TryReadEnum(row, "status", out GoalStatus status) ||
                !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId))
            {
                skipped++;
                continue;
            }

            if (existingByTitle.TryGetValue(NormalizeKey(title), out var existingId))
            {
                state.GoalIds[exportId] = existingId;
                reused++;
                continue;
            }

            var goal = new Goal
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Description = ReadOptionalString(row, "description", 2000),
                Status = status,
                TargetDate = ReadNullableDateTime(row, "targetDate"),
                AchievedDate = ReadNullableDateTime(row, "achievedDate"),
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                AreaId = areaId
            };

            if (state.Commit) context.Goals.Add(goal);
            state.GoalIds[exportId] = goal.Id;
            existingByTitle[NormalizeKey(title)] = goal.Id;
            created++;
        }

        state.RecordOutcome("Goals", created, reused, skipped);
    }

    private async Task ImportProjectsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "projects");
        var existing = await context.Projects.AsNoTracking()
            .Where(project => project.UserId == state.UserId)
            .Select(project => new { project.Id, project.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByName = BuildNameLookup(existing.Select(p => (p.Name, p.Id)), "Project", state.Conflicts);

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var name = ReadRequiredString(row, "name", 200);
            if (name is null ||
                !TryReadEnum(row, "status", out ProjectStatus status) ||
                !TryReadEnum(row, "priority", out ProjectPriority priority) ||
                !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId) ||
                !TryResolveOptionalReference(row, "goalId", state.GoalIds, out var goalId))
            {
                skipped++;
                continue;
            }

            if (existingByName.TryGetValue(NormalizeKey(name), out var existingId))
            {
                state.ProjectIds[exportId] = existingId;
                reused++;
                continue;
            }

            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Name = name,
                Emoji = ReadOptionalString(row, "emoji", 16) ?? ProjectEmojiDefaults.DefaultEmoji,
                Description = ReadOptionalString(row, "description", 2000),
                DesiredOutcome = ReadOptionalString(row, "desiredOutcome", 1000),
                Status = status,
                Priority = priority,
                StartDate = ReadNullableDateTime(row, "startDate"),
                DueDate = ReadNullableDateTime(row, "dueDate"),
                CompletedDate = ReadNullableDateTime(row, "completedDate"),
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                StatusBeforeArchive = ReadNullableEnum<ProjectStatus>(row, "statusBeforeArchive"),
                AreaId = areaId,
                GoalId = goalId
            };

            if (state.Commit) context.Projects.Add(project);
            state.ProjectIds[exportId] = project.Id;
            existingByName[NormalizeKey(name)] = project.Id;
            created++;
        }

        state.RecordOutcome("Projects", created, reused, skipped);
    }

    private async Task ImportResourcesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "resources");
        var existing = await context.Resources.AsNoTracking()
            .Where(resource => resource.UserId == state.UserId)
            .Select(resource => new { resource.Id, resource.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByName = BuildNameLookup(existing.Select(r => (r.Name, r.Id)), "Resource", state.Conflicts);

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var name = ReadRequiredString(row, "name", 200);
            if (name is null || !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId))
            {
                skipped++;
                continue;
            }

            if (existingByName.TryGetValue(NormalizeKey(name), out var existingId))
            {
                state.ResourceIds[exportId] = existingId;
                state.DuplicateResourceExportIds.Add(exportId);
                reused++;
                continue;
            }

            var resource = new Resource
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Name = name,
                Emoji = ReadOptionalString(row, "emoji", 16) ?? ResourceEmojiDefaults.DefaultEmoji,
                Description = ReadOptionalString(row, "description", 2000),
                Topic = ReadOptionalString(row, "topic", 200),
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                AreaId = areaId
            };

            if (state.Commit) context.Resources.Add(resource);
            state.ResourceIds[exportId] = resource.Id;
            state.NewResourceObjectsById[resource.Id] = resource;
            existingByName[NormalizeKey(name)] = resource.Id;
            created++;
        }

        state.RecordOutcome("Resources", created, reused, skipped);
    }

    private async Task ImportTagsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "tags");
        var existingTags = await context.Tags
            .Where(tag => tag.UserId == state.UserId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var tagLookup = existingTags.ToDictionary(tag => tag.Name, tag => tag, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existingTags)
            state.TagById[tag.Id] = tag;

        int created = 0, reused = 0, skipped = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var name = ReadRequiredString(row, "name", 100);
            if (name is null)
            {
                skipped++;
                continue;
            }

            if (tagLookup.TryGetValue(name, out var existingTag))
            {
                state.TagIds[exportId] = existingTag.Id;
                reused++;
                continue;
            }

            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Name = name,
                Color = ReadOptionalString(row, "color", 20)
            };

            if (state.Commit) context.Tags.Add(tag);
            tagLookup[tag.Name] = tag;
            state.TagById[tag.Id] = tag;
            state.TagIds[exportId] = tag.Id;
            created++;
        }

        state.RecordOutcome("Tags", created, reused, skipped);
    }

    private async Task ImportNotesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "notes");
        var noteTagLinksElement = GetArrayProperty(data, "noteTagLinks");

        var pendingNoteTagLinks = new Dictionary<Guid, List<Guid>>();
        foreach (var link in noteTagLinksElement.EnumerateArray())
        {
            if (TryReadGuid(link, "noteId", out var exportNoteId) && TryReadGuid(link, "tagId", out var exportTagId))
            {
                if (!pendingNoteTagLinks.TryGetValue(exportNoteId, out var list))
                {
                    list = [];
                    pendingNoteTagLinks[exportNoteId] = list;
                }

                list.Add(exportTagId);
            }
        }

        var existing = await context.Notes.AsNoTracking()
            .Where(note => note.UserId == state.UserId)
            .Select(note => new { note.Id, note.Title, note.Content })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var note in existing)
            existingByKey[NoteKey(note.Title, note.Content)] = note.Id;

        int created = 0, reused = 0, skipped = 0;
        var linkedNoteTags = 0;
        var skippedNoteTagLinks = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportNoteId))
            {
                skipped++;
                continue;
            }

            var title = ReadRequiredString(row, "title", 500);
            var contentText = row.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : null;

            if (title is null || contentText is null ||
                !TryReadEnum(row, "status", out NoteStatus status) ||
                !TryReadEnum(row, "paraCategory", out ParaCategory paraCategory) ||
                !TryResolveOptionalReference(row, "sourceId", state.SourceIds, out var sourceId) ||
                !TryResolveOptionalReference(row, "projectId", state.ProjectIds, out var projectId) ||
                !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId) ||
                !TryResolveOptionalReference(row, "resourceId", state.ResourceIds, out var resourceId))
            {
                skipped++;
                if (pendingNoteTagLinks.TryGetValue(exportNoteId, out var orphanedLinks))
                    skippedNoteTagLinks += orphanedLinks.Count;
                continue;
            }

            var key = NoteKey(title, contentText);
            if (existingByKey.TryGetValue(key, out var existingId))
            {
                state.NoteIds[exportNoteId] = existingId;
                state.DuplicateNoteExportIds.Add(exportNoteId);
                reused++;
                if (pendingNoteTagLinks.TryGetValue(exportNoteId, out var duplicateLinks))
                    skippedNoteTagLinks += duplicateLinks.Count;
                continue;
            }

            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Content = contentText,
                AiSummary = ReadOptionalString(row, "aiSummary", 4000),
                Status = status,
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                ProcessedAtUtc = ReadNullableDateTime(row, "processedAtUtc"),
                ParaCategory = paraCategory,
                SourceId = sourceId,
                ProjectId = projectId,
                AreaId = areaId,
                ResourceId = resourceId,
                IsFavorite = ReadBoolean(row, "isFavorite")
            };

            if (pendingNoteTagLinks.TryGetValue(exportNoteId, out var exportTagIds))
            {
                foreach (var exportTagId in exportTagIds)
                {
                    if (!state.TagIds.TryGetValue(exportTagId, out var currentTagId) ||
                        !state.TagById.TryGetValue(currentTagId, out var tag))
                    {
                        skippedNoteTagLinks++;
                        continue;
                    }

                    note.Tags.Add(tag);
                    linkedNoteTags++;
                }
            }

            if (state.Commit) context.Notes.Add(note);
            state.NoteIds[exportNoteId] = note.Id;
            state.NewNoteObjectsById[note.Id] = note;
            existingByKey[key] = note.Id;
            created++;
        }

        state.RecordOutcome("Notes", created, reused, skipped);
        state.RecordOutcome("Note tag links", linkedNoteTags, 0, skippedNoteTagLinks);
    }

    private static string NoteKey(string title, string content) => $"{title.Trim().ToLowerInvariant()}|{content}";

    private async Task ImportResourceTagLinksAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "resourceTagLinks");
        int created = 0, skipped = 0;
        var seen = new HashSet<(Guid ResourceId, Guid TagId)>();

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "resourceId", out var exportResourceId) || !TryReadGuid(row, "tagId", out var exportTagId))
            {
                skipped++;
                continue;
            }

            if (state.DuplicateResourceExportIds.Contains(exportResourceId))
            {
                skipped++;
                continue;
            }

            if (!state.ResourceIds.TryGetValue(exportResourceId, out var resourceId) ||
                !state.TagIds.TryGetValue(exportTagId, out var currentTagId) ||
                !state.TagById.TryGetValue(currentTagId, out var tag) ||
                !state.NewResourceObjectsById.TryGetValue(resourceId, out var resource))
            {
                skipped++;
                continue;
            }

            if (!seen.Add((resourceId, currentTagId)))
            {
                skipped++;
                continue;
            }

            if (state.Commit) resource.Tags.Add(tag);
            created++;
        }

        state.RecordOutcome("Resource tag links", created, 0, skipped);
    }

    private async Task ImportNoteImagesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "noteImages");
        var existingImages = await context.NoteImages.AsNoTracking()
            .Where(image => image.UserId == state.UserId)
            .Select(image => new { image.NoteId, image.Data })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingKeys = existingImages
            .Select(image => (image.NoteId, Sha256: Convert.ToHexString(SHA256.HashData(image.Data)).ToLowerInvariant()))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId) || !TryReadOptionalGuid(row, "noteId", out var exportNoteId))
            {
                skipped++;
                continue;
            }

            Guid? noteId = null;
            if (exportNoteId is { } id)
            {
                if (!state.NoteIds.TryGetValue(id, out var resolved))
                {
                    skipped++;
                    continue;
                }

                noteId = resolved;
            }

            var fileName = ReadRequiredString(row, "fileName", 255);
            var contentType = ReadRequiredString(row, "contentType", 100);
            var dataBase64 = row.TryGetProperty("dataBase64", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
                ? dataElement.GetString()
                : null;

            if (fileName is null || contentType is null || dataBase64 is null)
            {
                skipped++;
                continue;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(dataBase64);
            }
            catch (FormatException)
            {
                skipped++;
                continue;
            }

            var actualSha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
            var claimedSha256 = row.TryGetProperty("sha256", out var sha256Element) && sha256Element.ValueKind == JsonValueKind.String
                ? sha256Element.GetString()
                : null;
            if (claimedSha256 is not null && !string.Equals(claimedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                state.IntegrityIssues.Add(
                    $"Note image '{fileName}' (export id {exportId}) checksum did not match its bytes; imported using the actual bytes.");
            }

            if (!existingKeys.Add((noteId, actualSha256)))
            {
                skipped++;
                continue;
            }

            var image = new NoteImage
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                NoteId = noteId,
                FileName = Path.GetFileName(fileName),
                ContentType = contentType,
                SizeBytes = imageBytes.LongLength,
                Data = imageBytes
            };

            if (state.Commit) context.NoteImages.Add(image);
            created++;
        }

        state.RecordOutcome("Note images", created, 0, skipped);
    }

    private async Task ImportHighlightsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "highlights");
        var existingKeys = (await context.Highlights.AsNoTracking()
            .Where(highlight => highlight.Note.UserId == state.UserId)
            .Select(highlight => new { highlight.NoteId, highlight.Text })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(h => (h.NoteId, h.Text))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var text = row.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()
                : null;

            if (string.IsNullOrEmpty(text) || !TryResolveRequiredReference(row, "noteId", state.NoteIds, out var noteId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((noteId, text)))
            {
                skipped++;
                continue;
            }

            var highlight = new Highlight
            {
                Id = Guid.NewGuid(),
                NoteId = noteId,
                Text = text,
                Annotation = ReadOptionalString(row, "annotation", 2000),
                Layer = ReadNullableInt(row, "layer") ?? 1,
                StartOffset = ReadNullableInt(row, "startOffset"),
                EndOffset = ReadNullableInt(row, "endOffset")
            };

            if (state.Commit) context.Highlights.Add(highlight);
            created++;
        }

        state.RecordOutcome("Highlights", created, 0, skipped);
    }

    private async Task ImportSummariesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "summaries");
        var existingKeys = (await context.Summaries.AsNoTracking()
            .Where(summary => summary.Note.UserId == state.UserId)
            .Select(summary => new { summary.NoteId, summary.Content })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => (s.NoteId, s.Content))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var contentText = row.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : null;

            if (string.IsNullOrEmpty(contentText) || !TryResolveRequiredReference(row, "noteId", state.NoteIds, out var noteId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((noteId, contentText)))
            {
                skipped++;
                continue;
            }

            var summary = new Summary
            {
                Id = Guid.NewGuid(),
                NoteId = noteId,
                Content = contentText,
                IsAiGenerated = ReadBoolean(row, "isAiGenerated"),
                Model = ReadOptionalString(row, "model", 100),
                PromptVersion = ReadOptionalString(row, "promptVersion", 100)
            };

            if (state.Commit) context.Summaries.Add(summary);
            created++;
        }

        state.RecordOutcome("Summaries", created, 0, skipped);
    }

    private async Task ImportTasksAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "tasks").EnumerateArray().ToList();
        var existing = await context.Tasks.AsNoTracking()
            .Where(task => task.UserId == state.UserId)
            .Select(task => new { task.Id, task.ProjectId, task.Title })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var task in existing)
            existingByKey[TaskKey(task.ProjectId, task.Title)] = task.Id;

        int created = 0, reused = 0, skipped = 0;
        var pendingSelfReferences = new List<(TaskItem Task, Guid? ParentExportId, Guid? RecurrenceSourceExportId)>();

        foreach (var row in rows)
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var title = ReadRequiredString(row, "title", 500);
            if (title is null ||
                !TryReadEnum(row, "status", out TaskItemStatus status) ||
                !TryReadEnum(row, "priority", out TaskPriority priority) ||
                !TryResolveRequiredReference(row, "projectId", state.ProjectIds, out var projectId))
            {
                skipped++;
                continue;
            }

            var key = TaskKey(projectId, title);
            if (existingByKey.TryGetValue(key, out var existingId))
            {
                state.TaskIds[exportId] = existingId;
                reused++;
                continue;
            }

            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Description = ReadOptionalString(row, "description", 4000),
                Status = status,
                Priority = priority,
                DueDate = ReadNullableDateTime(row, "dueDate"),
                CompletedDate = ReadNullableDateTime(row, "completedDate"),
                IsArchived = ReadBoolean(row, "isArchived"),
                // A restored "current task" flag could collide with the importing account's
                // own current task and is transient UI state, not portable content, so it is
                // never re-asserted on import.
                IsCurrentTask = false,
                SortOrder = ReadNullableInt(row, "sortOrder") ?? 0,
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                ProjectId = projectId,
                Complexity = ReadNullableEnum<TaskComplexity>(row, "complexity"),
                IsRecurring = ReadBoolean(row, "isRecurring"),
                RecurrenceType = ReadNullableEnum<RecurrenceType>(row, "recurrenceType"),
                RecurrenceInterval = ReadNullableInt(row, "recurrenceInterval"),
                RecurrenceEndDate = ReadNullableDateTime(row, "recurrenceEndDate"),
                NextOccurrenceDate = ReadNullableDateTime(row, "nextOccurrenceDate")
            };

            TryReadOptionalGuid(row, "parentTaskId", out var parentExportId);
            TryReadOptionalGuid(row, "recurrenceSourceTaskId", out var recurrenceSourceExportId);
            pendingSelfReferences.Add((task, parentExportId, recurrenceSourceExportId));

            if (state.Commit) context.Tasks.Add(task);
            state.TaskIds[exportId] = task.Id;
            existingByKey[key] = task.Id;
            created++;
        }

        foreach (var (task, parentExportId, recurrenceSourceExportId) in pendingSelfReferences)
        {
            if (parentExportId is { } parentId && state.TaskIds.TryGetValue(parentId, out var currentParentId))
                task.ParentTaskId = currentParentId;

            if (recurrenceSourceExportId is { } sourceId && state.TaskIds.TryGetValue(sourceId, out var currentSourceId))
                task.RecurrenceSourceTaskId = currentSourceId;
        }

        state.RecordOutcome("Tasks", created, reused, skipped);
    }

    private static string TaskKey(Guid projectId, string title) => $"{projectId}|{title.Trim().ToLowerInvariant()}";

    private async Task ImportActionItemsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "actionItems");
        var existingKeys = (await context.ActionItems.AsNoTracking()
            .Where(action => action.UserId == state.UserId)
            .Select(action => new { action.NoteId, action.TaskItemId, action.Title })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(a => (a.NoteId, a.TaskItemId, a.Title))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var title = ReadRequiredString(row, "title", 500);
            if (title is null ||
                !TryReadEnum(row, "status", out ActionItemStatus status) ||
                !TryResolveOptionalReference(row, "noteId", state.NoteIds, out var noteId) ||
                !TryResolveOptionalReference(row, "taskItemId", state.TaskIds, out var taskItemId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((noteId, taskItemId, title)))
            {
                skipped++;
                continue;
            }

            var actionItem = new ActionItem
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Description = ReadOptionalString(row, "description", 2000),
                Status = status,
                IsAiGenerated = ReadBoolean(row, "isAiGenerated"),
                Model = ReadOptionalString(row, "model", 200),
                PromptVersion = ReadOptionalString(row, "promptVersion", 100),
                NoteId = noteId,
                TaskItemId = taskItemId
            };

            if (state.Commit) context.ActionItems.Add(actionItem);
            created++;
        }

        state.RecordOutcome("Action items", created, 0, skipped);
    }

    private async Task ImportNoteRelationshipsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "noteRelationships");
        var existingKeys = (await context.NoteRelationships.AsNoTracking()
            .Where(relationship => relationship.SourceNote.UserId == state.UserId && relationship.TargetNote.UserId == state.UserId)
            .Select(relationship => new { relationship.SourceNoteId, relationship.TargetNoteId, relationship.Type })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => (r.SourceNoteId, r.TargetNoteId, r.Type))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadEnum(row, "type", out RelationshipType type) ||
                !TryResolveRequiredReference(row, "sourceNoteId", state.NoteIds, out var sourceNoteId) ||
                !TryResolveRequiredReference(row, "targetNoteId", state.NoteIds, out var targetNoteId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((sourceNoteId, targetNoteId, type)))
            {
                skipped++;
                continue;
            }

            var relationship = new NoteRelationship
            {
                Id = Guid.NewGuid(),
                SourceNoteId = sourceNoteId,
                TargetNoteId = targetNoteId,
                Type = type,
                Annotation = ReadOptionalString(row, "annotation", 1000),
                IsAiGenerated = ReadBoolean(row, "isAiGenerated")
            };

            if (state.Commit) context.NoteRelationships.Add(relationship);
            created++;
        }

        state.RecordOutcome("Note relationships", created, 0, skipped);
    }

    private async Task ImportTaskDependenciesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "taskDependencies");
        var existingKeys = (await context.TaskDependencies.AsNoTracking()
            .Where(dependency => dependency.Task.UserId == state.UserId && dependency.DependsOnTask.UserId == state.UserId)
            .Select(dependency => new { dependency.TaskId, dependency.DependsOnTaskId })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(d => (d.TaskId, d.DependsOnTaskId))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryResolveRequiredReference(row, "taskId", state.TaskIds, out var taskId) ||
                !TryResolveRequiredReference(row, "dependsOnTaskId", state.TaskIds, out var dependsOnTaskId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((taskId, dependsOnTaskId)))
            {
                skipped++;
                continue;
            }

            var dependency = new TaskDependency
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                DependsOnTaskId = dependsOnTaskId
            };

            if (state.Commit) context.TaskDependencies.Add(dependency);
            created++;
        }

        state.RecordOutcome("Task dependencies", created, 0, skipped);
    }

    private async Task ImportOutputsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "outputs");
        var existing = await context.Outputs.AsNoTracking()
            .Where(output => output.UserId == state.UserId)
            .Select(output => new { output.Id, output.Title, output.Type })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var output in existing)
            existingByKey[OutputKey(output.Title, output.Type)] = output.Id;

        int created = 0, reused = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var title = ReadRequiredString(row, "title", 500);
            var contentText = row.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : null;

            if (title is null || contentText is null ||
                !TryReadEnum(row, "type", out OutputType type) ||
                !TryReadEnum(row, "status", out OutputStatus status) ||
                !TryResolveOptionalReference(row, "projectId", state.ProjectIds, out var projectId) ||
                !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId) ||
                !TryResolveOptionalReference(row, "goalId", state.GoalIds, out var goalId))
            {
                skipped++;
                continue;
            }

            var key = OutputKey(title, type);
            if (existingByKey.TryGetValue(key, out var existingId))
            {
                state.OutputIds[exportId] = existingId;
                state.DuplicateOutputExportIds.Add(exportId);
                reused++;
                continue;
            }

            var output = new Output
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Description = ReadOptionalString(row, "description", 2000),
                Content = contentText,
                Type = type,
                Status = status,
                IsAiGenerated = ReadBoolean(row, "isAiGenerated"),
                Model = ReadOptionalString(row, "model", 100),
                PromptVersion = ReadOptionalString(row, "promptVersion", 100),
                ProjectId = projectId,
                AreaId = areaId,
                GoalId = goalId,
                PublishedDate = ReadNullableDateTime(row, "publishedDate"),
                ArchivedDate = ReadNullableDateTime(row, "archivedDate"),
                IsArchived = ReadBoolean(row, "isArchived")
            };

            if (state.Commit) context.Outputs.Add(output);
            state.OutputIds[exportId] = output.Id;
            state.NewOutputObjectsById[output.Id] = output;
            existingByKey[key] = output.Id;
            created++;
        }

        state.RecordOutcome("Outputs", created, reused, skipped);
    }

    private static string OutputKey(string title, OutputType type) => $"{type}|{title.Trim().ToLowerInvariant()}";

    private async Task ImportOutputSourceNoteLinksAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "outputSourceNoteLinks");
        int created = 0, skipped = 0;
        var seen = new HashSet<(Guid OutputId, Guid NoteId)>();

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "outputId", out var exportOutputId) || !TryReadGuid(row, "noteId", out var exportNoteId))
            {
                skipped++;
                continue;
            }

            if (state.DuplicateOutputExportIds.Contains(exportOutputId))
            {
                skipped++;
                continue;
            }

            if (!state.OutputIds.TryGetValue(exportOutputId, out var outputId) ||
                !state.NoteIds.TryGetValue(exportNoteId, out var noteId) ||
                !state.NewOutputObjectsById.TryGetValue(outputId, out var output))
            {
                skipped++;
                continue;
            }

            if (!seen.Add((outputId, noteId)))
            {
                skipped++;
                continue;
            }

            if (state.Commit) output.SourceNotes.Add(ResolveNoteForLinking(state, noteId));
            created++;
        }

        state.RecordOutcome("Output source note links", created, 0, skipped);
    }

    private Note ResolveNoteForLinking(ImportState state, Guid noteId)
    {
        if (state.NewNoteObjectsById.TryGetValue(noteId, out var newNote))
            return newNote;

        if (state.AttachedNoteStubs.TryGetValue(noteId, out var stub))
            return stub;

        var attached = new Note { Id = noteId };
        context.Entry(attached).State = EntityState.Unchanged;
        state.AttachedNoteStubs[noteId] = attached;
        return attached;
    }

    private async Task ImportIdeasAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "ideas");
        var existing = await context.Ideas.AsNoTracking()
            .Where(idea => idea.UserId == state.UserId)
            .Select(idea => new { idea.Id, idea.Title })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByTitle = BuildNameLookup(existing.Select(i => (i.Title, i.Id)), "Idea", state.Conflicts);

        int created = 0, reused = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            if (!TryReadGuid(row, "id", out var exportId))
            {
                skipped++;
                continue;
            }

            var title = ReadRequiredString(row, "title", 300);
            if (title is null ||
                !TryReadEnum(row, "priority", out IdeaPriority priority) ||
                !TryReadEnum(row, "status", out IdeaStatus status) ||
                !TryResolveOptionalReference(row, "areaId", state.AreaIds, out var areaId) ||
                !TryResolveOptionalReference(row, "committedProjectId", state.ProjectIds, out var committedProjectId))
            {
                skipped++;
                continue;
            }

            if (existingByTitle.TryGetValue(NormalizeKey(title), out var existingId))
            {
                state.IdeaIds[exportId] = existingId;
                reused++;
                continue;
            }

            var idea = new Idea
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                Title = title,
                Description = ReadOptionalString(row, "description", 2000),
                AreaId = areaId,
                Priority = priority,
                Status = status,
                IsArchived = ReadBoolean(row, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(row, "archivedAtUtc"),
                Research = ReadOptionalString(row, "research", int.MaxValue),
                Competitors = ReadOptionalString(row, "competitors", int.MaxValue),
                Notes = ReadOptionalString(row, "notes", int.MaxValue),
                TargetUserAndProblem = ReadOptionalString(row, "targetUserAndProblem", int.MaxValue),
                SuitabilityReason = ReadOptionalString(row, "suitabilityReason", int.MaxValue),
                Evidence = ReadOptionalString(row, "evidence", int.MaxValue),
                ValidationExperiment = ReadOptionalString(row, "validationExperiment", int.MaxValue),
                ReplacedCommitment = ReadOptionalString(row, "replacedCommitment", int.MaxValue),
                CommittedProjectId = committedProjectId,
                CommittedAtUtc = ReadNullableDateTime(row, "committedAtUtc")
            };

            if (state.Commit) context.Ideas.Add(idea);
            state.IdeaIds[exportId] = idea.Id;
            existingByTitle[NormalizeKey(title)] = idea.Id;
            created++;
        }

        state.RecordOutcome("Ideas", created, reused, skipped);
    }

    private async Task ImportGoalMilestonesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "goalMilestones");
        var existingKeys = (await context.GoalMilestones.AsNoTracking()
            .Where(milestone => milestone.Goal != null && milestone.Goal.UserId == state.UserId)
            .Select(milestone => new { milestone.GoalId, milestone.Title })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => (m.GoalId, m.Title))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var title = ReadRequiredString(row, "title", 200);
            if (title is null || !TryResolveRequiredReference(row, "goalId", state.GoalIds, out var goalId))
            {
                skipped++;
                continue;
            }

            if (!existingKeys.Add((goalId, title)))
            {
                skipped++;
                continue;
            }

            var milestone = new GoalMilestone
            {
                Id = Guid.NewGuid(),
                GoalId = goalId,
                Title = title,
                IsCompleted = ReadBoolean(row, "isCompleted"),
                CompletedAtUtc = ReadNullableDateTime(row, "completedAtUtc")
            };

            if (state.Commit) context.GoalMilestones.Add(milestone);
            created++;
        }

        state.RecordOutcome("Goal milestones", created, 0, skipped);
    }

    private async Task ImportGoalActivitiesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "goalActivities");
        var existingKeys = (await context.GoalActivities.AsNoTracking()
            .Where(activity => activity.Goal.UserId == state.UserId)
            .Select(activity => new { activity.GoalId, activity.ActivityType, activity.Description, activity.OldValue, activity.NewValue })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(a => (a.GoalId, a.ActivityType, a.Description, a.OldValue, a.NewValue))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var description = ReadRequiredString(row, "description", 500);
            if (description is null ||
                !TryReadEnum(row, "activityType", out GoalActivityType activityType) ||
                !TryResolveRequiredReference(row, "goalId", state.GoalIds, out var goalId))
            {
                skipped++;
                continue;
            }

            var oldValue = ReadOptionalString(row, "oldValue", 500);
            var newValue = ReadOptionalString(row, "newValue", 500);

            if (!existingKeys.Add((goalId, activityType, description, oldValue, newValue)))
            {
                skipped++;
                continue;
            }

            var activity = new GoalActivity
            {
                Id = Guid.NewGuid(),
                GoalId = goalId,
                ActivityType = activityType,
                Description = description,
                OldValue = oldValue,
                NewValue = newValue
            };

            if (state.Commit) context.GoalActivities.Add(activity);
            created++;
        }

        state.RecordOutcome("Goal activities", created, 0, skipped);
    }

    private async Task ImportArchiveRetentionRulesAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "archiveRetentionRules");
        var existingTypes = new HashSet<string>(
            await context.ArchiveRetentionRules.AsNoTracking()
                .Where(rule => rule.UserId == state.UserId)
                .Select(rule => rule.EntityType)
                .ToListAsync(cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);

        int created = 0, reused = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var entityType = ReadRequiredString(row, "entityType", 50);
            if (entityType is null)
            {
                skipped++;
                continue;
            }

            if (existingTypes.Contains(entityType))
            {
                reused++;
                continue;
            }

            var rule = new ArchiveRetentionRule
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                EntityType = entityType,
                RetentionDays = ReadNullableInt(row, "retentionDays")
            };

            if (state.Commit) context.ArchiveRetentionRules.Add(rule);
            existingTypes.Add(entityType);
            created++;
        }

        state.RecordOutcome("Archive retention rules", created, reused, skipped);
    }

    private async Task ImportWeeklyTaskSelectionsAsync(JsonElement data, ImportState state, CancellationToken cancellationToken)
    {
        var rows = GetArrayProperty(data, "weeklyTaskSelections");
        var existingKeys = (await context.WeeklyTaskSelections.AsNoTracking()
            .Where(selection => selection.UserId == state.UserId)
            .Select(selection => new { selection.TaskId, selection.WeekStartDate })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => (s.TaskId, s.WeekStartDate))
            .ToHashSet();

        int created = 0, skipped = 0;

        foreach (var row in rows.EnumerateArray())
        {
            var weekStartDate = ReadNullableDateTime(row, "weekStartDate");
            if (weekStartDate is null || !TryResolveRequiredReference(row, "taskId", state.TaskIds, out var taskId))
            {
                skipped++;
                continue;
            }

            var key = (taskId, weekStartDate.Value.Date);
            if (!existingKeys.Add(key))
            {
                skipped++;
                continue;
            }

            var selection = new WeeklyTaskSelection
            {
                Id = Guid.NewGuid(),
                UserId = state.UserId,
                TaskId = taskId,
                WeekStartDate = weekStartDate.Value.Date
            };

            if (state.Commit) context.WeeklyTaskSelections.Add(selection);
            created++;
        }

        state.RecordOutcome("Weekly task selections", created, 0, skipped);
    }

    private static void ValidateRoot(JsonElement root)
    {
        if (!root.TryGetProperty("product", out var productElement) ||
            !string.Equals(productElement.GetString(), "Brainy", StringComparison.Ordinal) ||
            !root.TryGetProperty("schemaVersion", out _) ||
            !root.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The uploaded file is not a valid Brainy export.");
        }
    }

    private static string ValidateSchemaVersion(JsonElement root)
    {
        var schemaVersion = root.GetProperty("schemaVersion").GetString();
        if (!string.Equals(schemaVersion, IDataExportService.SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported export schema '{schemaVersion}'. Brainy currently imports schema {IDataExportService.SchemaVersion} files only.");
        }

        return schemaVersion!;
    }

    private static IReadOnlyList<DataImportEntityCountDto> ComputeUnsupported(JsonElement data) =>
        data.EnumerateObject()
            .Where(property => !SupportedSections.Contains(property.Name) &&
                                property.Value.ValueKind == JsonValueKind.Array &&
                                property.Value.GetArrayLength() > 0)
            .Select(property => new DataImportEntityCountDto(
                SectionLabels.GetValueOrDefault(property.Name, property.Name),
                property.Value.GetArrayLength()))
            .OrderBy(item => item.EntityType, StringComparer.Ordinal)
            .ToArray();

    private static JsonElement GetArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The uploaded export is missing the '{propertyName}' collection.");
        }

        return propertyValue;
    }

    private static Dictionary<string, Guid> BuildNameLookup(
        IEnumerable<(string Name, Guid Id)> rows, string entityType, ICollection<string> conflicts)
    {
        var lookup = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (name, id) in rows)
        {
            var key = NormalizeKey(name);
            if (!lookup.TryAdd(key, id))
            {
                conflicts.Add(
                    $"Multiple existing {entityType} rows are named '{name}'. New rows with this name will be matched to one of them arbitrarily.");
            }
        }

        return lookup;
    }

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(propertyName, out var propertyValue) &&
               propertyValue.ValueKind == JsonValueKind.String &&
               Guid.TryParse(propertyValue.GetString(), out value);
    }

    private static bool TryReadOptionalGuid(JsonElement element, string propertyName, out Guid? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (propertyValue.ValueKind == JsonValueKind.String && Guid.TryParse(propertyValue.GetString(), out var guid))
        {
            value = guid;
            return true;
        }

        return false;
    }

    private static bool TryResolveOptionalReference(
        JsonElement element, string propertyName, IReadOnlyDictionary<Guid, Guid> remap, out Guid? resolved)
    {
        resolved = null;
        if (!TryReadOptionalGuid(element, propertyName, out var exportId)) return false;
        if (exportId is null) return true;
        if (!remap.TryGetValue(exportId.Value, out var currentId)) return false;
        resolved = currentId;
        return true;
    }

    private static bool TryResolveRequiredReference(
        JsonElement element, string propertyName, IReadOnlyDictionary<Guid, Guid> remap, out Guid resolved)
    {
        resolved = Guid.Empty;
        return TryReadGuid(element, propertyName, out var exportId) && remap.TryGetValue(exportId, out resolved);
    }

    private static bool TryReadEnum<TEnum>(JsonElement element, string propertyName, out TEnum value)
        where TEnum : struct
    {
        value = default;
        return element.TryGetProperty(propertyName, out var propertyValue) &&
               propertyValue.ValueKind == JsonValueKind.String &&
               Enum.TryParse(propertyValue.GetString(), ignoreCase: true, out value);
    }

    private static TEnum? ReadNullableEnum<TEnum>(JsonElement element, string propertyName) where TEnum : struct =>
        element.TryGetProperty(propertyName, out var propertyValue) &&
        propertyValue.ValueKind == JsonValueKind.String &&
        Enum.TryParse<TEnum>(propertyValue.GetString(), ignoreCase: true, out var value)
            ? value
            : null;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var propertyValue) &&
        propertyValue.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        propertyValue.GetBoolean();

    private static int? ReadNullableInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var propertyValue) &&
        propertyValue.ValueKind == JsonValueKind.Number &&
        propertyValue.TryGetInt32(out var value)
            ? value
            : null;

    private static string? ReadRequiredString(JsonElement element, string propertyName, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = propertyValue.GetString();
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > maxLength ? null : value;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = propertyValue.GetString();
        return string.IsNullOrWhiteSpace(value) || value.Length > maxLength
            ? null
            : value;
    }

    private static DateTime? ReadNullableDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return propertyValue.ValueKind == JsonValueKind.String &&
               propertyValue.TryGetDateTime(out var value)
            ? value
            : null;
    }

    private sealed class ImportState(string userId, bool commit)
    {
        public string UserId { get; } = userId;
        public bool Commit { get; } = commit;

        public Dictionary<Guid, Guid> AreaIds { get; } = [];
        public Dictionary<Guid, Guid> SourceIds { get; } = [];
        public Dictionary<Guid, Guid> GoalIds { get; } = [];
        public Dictionary<Guid, Guid> ProjectIds { get; } = [];
        public Dictionary<Guid, Guid> ResourceIds { get; } = [];
        public Dictionary<Guid, Guid> TagIds { get; } = [];
        public Dictionary<Guid, Guid> NoteIds { get; } = [];
        public Dictionary<Guid, Guid> TaskIds { get; } = [];
        public Dictionary<Guid, Guid> OutputIds { get; } = [];
        public Dictionary<Guid, Guid> IdeaIds { get; } = [];

        public HashSet<Guid> DuplicateNoteExportIds { get; } = [];
        public HashSet<Guid> DuplicateResourceExportIds { get; } = [];
        public HashSet<Guid> DuplicateOutputExportIds { get; } = [];

        public Dictionary<Guid, Tag> TagById { get; } = [];
        public Dictionary<Guid, Note> NewNoteObjectsById { get; } = [];
        public Dictionary<Guid, Note> AttachedNoteStubs { get; } = [];
        public Dictionary<Guid, Resource> NewResourceObjectsById { get; } = [];
        public Dictionary<Guid, Output> NewOutputObjectsById { get; } = [];

        public List<string> Conflicts { get; } = [];
        public List<string> IntegrityIssues { get; } = [];

        private readonly List<DataImportEntityOutcomeDto> _outcomes = [];

        public void RecordOutcome(string entityType, int created, int reused, int skipped) =>
            _outcomes.Add(new DataImportEntityOutcomeDto(entityType, created, reused, skipped));

        public IReadOnlyList<DataImportEntityOutcomeDto> BuildOutcomes() => _outcomes;
    }
}
