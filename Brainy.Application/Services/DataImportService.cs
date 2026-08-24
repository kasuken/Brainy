using System.Text.Json;
using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

internal sealed class DataImportService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : IDataImportService
{
    private static readonly HashSet<string> SupportedSections = ["tags", "notes", "noteTagLinks"];

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
        ["weeklyTaskSelections"] = "Weekly task selections"
    };

    public async Task<DataImportResultDto> ImportCurrentUserAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateRoot(root);

        var schemaVersion = root.GetProperty("schemaVersion").GetString();
        if (!string.Equals(schemaVersion, IDataExportService.SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported export schema '{schemaVersion}'. Brainy currently imports schema {IDataExportService.SchemaVersion} files only.");
        }

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var data = root.GetProperty("data");
        var tagsElement = GetArrayProperty(data, "tags");
        var notesElement = GetArrayProperty(data, "notes");
        var noteTagLinksElement = GetArrayProperty(data, "noteTagLinks");

        var unsupported = data.EnumerateObject()
            .Where(property => !SupportedSections.Contains(property.Name) &&
                               property.Value.ValueKind == JsonValueKind.Array &&
                               property.Value.GetArrayLength() > 0)
            .Select(property => new DataImportEntityCountDto(
                SectionLabels.GetValueOrDefault(property.Name, property.Name),
                property.Value.GetArrayLength()))
            .OrderBy(item => item.EntityType, StringComparer.Ordinal)
            .ToArray();

        var existingTags = await context.Tags
            .Where(tag => tag.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var tagLookup = existingTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var tagById = existingTags.ToDictionary(tag => tag.Id);
        var importedTagIds = new Dictionary<Guid, Guid>();

        var importedTags = 0;
        var reusedTags = 0;
        var skippedTags = 0;

        foreach (var tagElement in tagsElement.EnumerateArray())
        {
            if (!TryReadGuid(tagElement, "id", out var exportTagId))
            {
                skippedTags++;
                continue;
            }

            var tagName = tagElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(tagName) || tagName.Length > 100)
            {
                skippedTags++;
                continue;
            }

            if (tagLookup.TryGetValue(tagName, out var existingTag))
            {
                importedTagIds[exportTagId] = existingTag.Id;
                reusedTags++;
                continue;
            }

            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = tagName,
                Color = ReadOptionalString(tagElement, "color", maxLength: 20)
            };
            context.Tags.Add(tag);
            tagLookup[tag.Name] = tag;
            tagById[tag.Id] = tag;
            importedTagIds[exportTagId] = tag.Id;
            importedTags++;
        }

        var importedNotes = 0;
        var skippedNotes = 0;
        var linkedNoteTags = 0;
        var skippedNoteTagLinks = 0;
        var importedNoteIds = new HashSet<Guid>();
        var pendingNoteTagLinks = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var noteLinkElement in noteTagLinksElement.EnumerateArray())
        {
            if (TryReadGuid(noteLinkElement, "noteId", out var exportNoteId) &&
                TryReadGuid(noteLinkElement, "tagId", out var exportTagId))
            {
                if (!pendingNoteTagLinks.TryGetValue(exportNoteId, out var tagIds))
                {
                    tagIds = [];
                    pendingNoteTagLinks[exportNoteId] = tagIds;
                }

                tagIds.Add(exportTagId);
            }
            else
            {
                skippedNoteTagLinks++;
            }
        }

        foreach (var noteElement in notesElement.EnumerateArray())
        {
            if (!TryReadGuid(noteElement, "id", out var exportNoteId))
            {
                skippedNotes++;
                continue;
            }

            if (HasUnsupportedNoteRelationships(noteElement))
            {
                skippedNotes++;
                continue;
            }

            var title = noteElement.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()?.Trim()
                : null;
            var contentText = noteElement.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : null;
            var aiSummary = ReadOptionalString(noteElement, "aiSummary", maxLength: 4000);

            if (string.IsNullOrWhiteSpace(title) || title.Length > 500 || contentText is null)
            {
                skippedNotes++;
                continue;
            }

            if (!TryReadEnum(noteElement, "status", out NoteStatus status) ||
                !TryReadEnum(noteElement, "paraCategory", out ParaCategory paraCategory))
            {
                skippedNotes++;
                continue;
            }

            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Content = contentText,
                AiSummary = aiSummary,
                Status = status,
                IsArchived = ReadBoolean(noteElement, "isArchived"),
                ArchivedAtUtc = ReadNullableDateTime(noteElement, "archivedAtUtc"),
                ProcessedAtUtc = ReadNullableDateTime(noteElement, "processedAtUtc"),
                ParaCategory = paraCategory,
                IsFavorite = ReadBoolean(noteElement, "isFavorite")
            };

            if (pendingNoteTagLinks.TryGetValue(exportNoteId, out var exportTagIds))
            {
                foreach (var exportTagId in exportTagIds)
                {
                    if (!importedTagIds.TryGetValue(exportTagId, out var currentTagId))
                    {
                        skippedNoteTagLinks++;
                        continue;
                    }

                    var tag = tagById[currentTagId];
                    note.Tags.Add(tag);
                    linkedNoteTags++;
                }
            }

            context.Notes.Add(note);
            importedNoteIds.Add(exportNoteId);
            importedNotes++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        skippedNoteTagLinks += pendingNoteTagLinks
            .Where(entry => !importedNoteIds.Contains(entry.Key))
            .Sum(entry => entry.Value.Count);

        return new DataImportResultDto(
            schemaVersion!,
            importedTags,
            reusedTags,
            skippedTags,
            importedNotes,
            skippedNotes,
            linkedNoteTags,
            skippedNoteTagLinks,
            unsupported);
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

    private static JsonElement GetArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The uploaded export is missing the '{propertyName}' collection.");
        }

        return propertyValue;
    }

    private static bool HasUnsupportedNoteRelationships(JsonElement noteElement) =>
        HasGuidValue(noteElement, "sourceId") ||
        HasGuidValue(noteElement, "projectId") ||
        HasGuidValue(noteElement, "areaId") ||
        HasGuidValue(noteElement, "resourceId");

    private static bool HasGuidValue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var propertyValue) &&
        propertyValue.ValueKind == JsonValueKind.String &&
        Guid.TryParse(propertyValue.GetString(), out _);

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(propertyName, out var propertyValue) &&
               propertyValue.ValueKind == JsonValueKind.String &&
               Guid.TryParse(propertyValue.GetString(), out value);
    }

    private static bool TryReadEnum<TEnum>(JsonElement element, string propertyName, out TEnum value)
        where TEnum : struct
    {
        value = default;
        return element.TryGetProperty(propertyName, out var propertyValue) &&
               propertyValue.ValueKind == JsonValueKind.String &&
               Enum.TryParse(propertyValue.GetString(), ignoreCase: true, out value);
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var propertyValue) &&
        propertyValue.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        propertyValue.GetBoolean();

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
}
