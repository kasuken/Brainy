using System.IO.Compression;
using System.Text;
using Brainy.Application.DTOs.DataExport;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Produces a Markdown/Obsidian-compatible vault for one user: notes organised into PARA
/// category folders, YAML front matter carrying tags/dates/status/provenance, [[wiki links]]
/// derived from <see cref="Brainy.Domain.Entities.NoteRelationship"/>, and note images copied
/// alongside with content rewritten to reference them by relative path. Complements
/// <see cref="IDataExportService"/>; it never replaces or alters the JSON export.
/// </summary>
internal sealed class MarkdownExportService(
    IApplicationDbContext context,
    TimeProvider timeProvider) : IMarkdownExportService
{
    private const string ZipContentType = "application/zip";
    private const string ProjectsFolder = "Projects";
    private const string AreasFolder = "Areas";
    private const string ResourcesFolder = "Resources";
    private const string ArchiveFolder = "Archive";
    private const string AttachmentsFolder = "attachments";
    private const string UnfiledFolderName = "Unfiled";

    public async Task<MarkdownExportFileDto> ExportUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var exportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var projects = (await context.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => new ContainerRow(p.Id, p.Name))
            .ToList();

        var areas = (await context.Areas.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(a => new ContainerRow(a.Id, a.Name))
            .ToList();

        var resources = (await context.Resources.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => new ContainerRow(r.Id, r.Name))
            .ToList();

        var notes = (await context.Notes.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAtUtc).ThenBy(n => n.Id)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Content,
                n.Status,
                n.IsArchived,
                n.ArchivedAtUtc,
                n.ParaCategory,
                n.IsFavorite,
                n.CreatedAtUtc,
                n.UpdatedAtUtc,
                n.ProjectId,
                n.AreaId,
                n.ResourceId,
                n.SourceId,
                SourceType = n.Source != null ? (SourceType?)n.Source.Type : null,
                SourceTitle = n.Source != null ? n.Source.Title : null,
                SourceUrl = n.Source != null ? n.Source.Url : null,
                SourceAuthor = n.Source != null ? n.Source.Author : null,
                SourceCapturedAtUtc = n.Source != null ? n.Source.CapturedAtUtc : null,
                Tags = n.Tags
                    .Where(t => t.UserId == userId)
                    .OrderBy(t => t.Name)
                    .Select(t => t.Name)
                    .ToList()
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(n => new NoteRow(
                n.Id, n.Title, n.Content, n.Status, n.IsArchived, n.ArchivedAtUtc, n.ParaCategory,
                n.IsFavorite, n.CreatedAtUtc, n.UpdatedAtUtc, n.ProjectId, n.AreaId, n.ResourceId,
                n.SourceId, n.SourceType, n.SourceTitle, n.SourceUrl, n.SourceAuthor,
                n.SourceCapturedAtUtc, n.Tags))
            .ToList();

        var images = (await context.NoteImages.AsNoTracking()
            .Where(i => i.UserId == userId && i.NoteId != null)
            .OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id)
            .Select(i => new { i.Id, NoteId = i.NoteId!.Value, i.FileName, i.ContentType, i.Data })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(i => new ImageRow(i.Id, i.NoteId, i.FileName, i.ContentType, i.Data))
            .ToList();

        var relationships = (await context.NoteRelationships.AsNoTracking()
            .Where(r => r.SourceNote.UserId == userId && r.TargetNote.UserId == userId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.SourceNoteId, r.TargetNoteId, r.Type })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => new RelationshipRow(r.Id, r.SourceNoteId, r.TargetNoteId, r.Type))
            .ToList();

        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);
        var areaNames = areas.ToDictionary(a => a.Id, a => a.Name);
        var resourceNames = resources.ToDictionary(r => r.Id, r => r.Name);

        var imagesByNote = images
            .GroupBy(i => i.NoteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var outgoingByNote = relationships
            .GroupBy(r => r.SourceNoteId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var incomingByNote = relationships
            .GroupBy(r => r.TargetNoteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Folder names are allocated once per container (not once per note in it), in a
        // deterministic order, so every note that belongs to the same project/area/resource
        // lands in the same folder instead of each minting its own collision suffix.
        var folderAllocator = new UniqueNameAllocator();
        var projectFolders = AllocateContainerFolders(projects, ProjectsFolder, folderAllocator);
        var areaFolders = AllocateContainerFolders(areas, AreasFolder, folderAllocator);
        var resourceFolders = AllocateContainerFolders(resources, ResourcesFolder, folderAllocator);

        // Note file names are unique vault-wide (not just per folder) so a bare [[Title]]
        // wiki link always resolves to exactly one file, regardless of which folders the
        // source and target notes live in.
        var noteNameAllocator = new UniqueNameAllocator();
        const string noteScope = "notes";
        var notePlans = new Dictionary<Guid, NotePlan>();

        foreach (var note in notes)
        {
            var folderPath = note.ParaCategory switch
            {
                ParaCategory.Project => CombineFolder(
                    ProjectsFolder, ResolveContainerFolder(note.ProjectId, projectFolders)),
                ParaCategory.Area => CombineFolder(
                    AreasFolder, ResolveContainerFolder(note.AreaId, areaFolders)),
                ParaCategory.Resource => CombineFolder(
                    ResourcesFolder, ResolveContainerFolder(note.ResourceId, resourceFolders)),
                _ => ArchiveFolder
            };

            var stem = MarkdownFileNameSanitizer.Sanitize(note.Title);
            var uniqueStem = noteNameAllocator.Allocate(noteScope, stem);

            notePlans[note.Id] = new NotePlan(note.Title, folderPath, uniqueStem);
        }

        // Attachment file names are also unique vault-wide: every image lives in one shared
        // "attachments" folder regardless of which note it belongs to.
        var imageAllocator = new UniqueNameAllocator();
        const string attachmentScope = "attachments";
        var imagePlans = new Dictionary<Guid, string>();

        foreach (var image in images)
        {
            var extension = ResolveImageExtension(image.FileName, image.ContentType);
            var stem = MarkdownFileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(image.FileName));
            var uniqueName = imageAllocator.Allocate(attachmentScope, stem) + extension;
            imagePlans[image.Id] = uniqueName;
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteReadme(archive, exportedAtUtc);

            foreach (var note in notes)
            {
                var plan = notePlans[note.Id];
                var noteImages = imagesByNote.GetValueOrDefault(note.Id, []);
                var outgoing = outgoingByNote.GetValueOrDefault(note.Id, []);
                var incoming = incomingByNote.GetValueOrDefault(note.Id, []);

                var body = BuildNoteMarkdown(
                    note,
                    plan,
                    noteImages,
                    imagePlans,
                    outgoing,
                    incoming,
                    notePlans,
                    ContainerName(note.ProjectId, projectNames),
                    ContainerName(note.AreaId, areaNames),
                    ContainerName(note.ResourceId, resourceNames));

                var entryPath = $"{plan.FolderPath}/{plan.FileStem}.md";
                WriteTextEntry(archive, entryPath, body);
            }

            foreach (var image in images)
            {
                var entryPath = $"{AttachmentsFolder}/{imagePlans[image.Id]}";
                WriteBinaryEntry(archive, entryPath, image.Data);
            }
        }

        var fileName = $"brainy-vault-export-{exportedAtUtc:yyyyMMdd-HHmmss}Z.zip";
        return new MarkdownExportFileDto(fileName, ZipContentType, zipStream.ToArray());
    }

    private static Dictionary<Guid, string> AllocateContainerFolders(
        List<ContainerRow> containers,
        string categoryScope,
        UniqueNameAllocator allocator)
    {
        // Reserve the literal "Unfiled" name first so a real container that happens to be
        // named "Unfiled" gets disambiguated instead of colliding with the fallback folder.
        allocator.Allocate(categoryScope, UnfiledFolderName);

        var result = new Dictionary<Guid, string>();
        foreach (var container in containers)
        {
            var stem = MarkdownFileNameSanitizer.Sanitize(container.Name);
            result[container.Id] = allocator.Allocate(categoryScope, stem);
        }

        return result;
    }

    private static string ResolveContainerFolder(Guid? containerId, Dictionary<Guid, string> folders)
        => containerId.HasValue && folders.TryGetValue(containerId.Value, out var name)
            ? name
            : UnfiledFolderName;

    private static string? ContainerName(Guid? containerId, Dictionary<Guid, string> names)
        => containerId.HasValue && names.TryGetValue(containerId.Value, out var name) ? name : null;

    private static string CombineFolder(string category, string container) => $"{category}/{container}";

    private static string BuildNoteMarkdown(
        NoteRow note,
        NotePlan plan,
        List<ImageRow> noteImages,
        Dictionary<Guid, string> imagePlans,
        List<RelationshipRow> outgoing,
        List<RelationshipRow> incoming,
        Dictionary<Guid, NotePlan> notePlans,
        string? projectName,
        string? areaName,
        string? resourceName)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("title: ").Append(YamlWriter.QuotedString(note.Title)).Append('\n');
        builder.Append("brainy_id: ").Append(YamlWriter.QuotedString(note.Id.ToString())).Append('\n');
        builder.Append("para: ").Append(YamlWriter.QuotedString(note.ParaCategory.ToString().ToLowerInvariant())).Append('\n');
        builder.Append("status: ").Append(YamlWriter.QuotedString(note.Status.ToString().ToLowerInvariant())).Append('\n');
        builder.Append("favorite: ").Append(YamlWriter.Bool(note.IsFavorite)).Append('\n');
        builder.Append("archived: ").Append(YamlWriter.Bool(note.IsArchived)).Append('\n');

        if (note.ArchivedAtUtc is { } archivedAtUtc)
            builder.Append("archived_at: ").Append(YamlWriter.UtcTimestamp(archivedAtUtc)).Append('\n');

        builder.Append("created: ").Append(YamlWriter.UtcTimestamp(note.CreatedAtUtc)).Append('\n');
        builder.Append("updated: ").Append(YamlWriter.UtcTimestamp(note.UpdatedAtUtc)).Append('\n');
        builder.Append("tags: ").Append(YamlWriter.StringArray(note.Tags)).Append('\n');

        if (projectName is not null)
            builder.Append("project: ").Append(YamlWriter.QuotedString(projectName)).Append('\n');
        if (areaName is not null)
            builder.Append("area: ").Append(YamlWriter.QuotedString(areaName)).Append('\n');
        if (resourceName is not null)
            builder.Append("resource: ").Append(YamlWriter.QuotedString(resourceName)).Append('\n');

        if (note.SourceId is not null)
        {
            builder.Append("source:\n");
            if (note.SourceType is { } sourceType)
                builder.Append("  type: ").Append(YamlWriter.QuotedString(sourceType.ToString().ToLowerInvariant())).Append('\n');
            if (note.SourceTitle is not null)
                builder.Append("  title: ").Append(YamlWriter.QuotedString(note.SourceTitle)).Append('\n');
            if (note.SourceUrl is not null)
                builder.Append("  url: ").Append(YamlWriter.QuotedString(note.SourceUrl)).Append('\n');
            if (note.SourceAuthor is not null)
                builder.Append("  author: ").Append(YamlWriter.QuotedString(note.SourceAuthor)).Append('\n');
            if (note.SourceCapturedAtUtc is { } capturedAtUtc)
                builder.Append("  captured_at: ").Append(YamlWriter.UtcTimestamp(capturedAtUtc)).Append('\n');
        }

        builder.Append("---\n\n");
        builder.Append("# ").Append(note.Title).Append("\n\n");

        var depth = plan.FolderPath.Count(c => c == '/') + 1;
        var upPrefix = string.Concat(Enumerable.Repeat("../", depth));

        var content = note.Content;
        var referencedImageIds = new HashSet<Guid>();
        foreach (var image in noteImages)
        {
            var marker = $"/api/note-images/{image.Id}";
            if (!content.Contains(marker, StringComparison.Ordinal))
                continue;

            referencedImageIds.Add(image.Id);
            var relativePath = upPrefix + AttachmentsFolder + "/" + EncodeMarkdownLinkPath(imagePlans[image.Id]);
            content = content.Replace(marker, relativePath, StringComparison.Ordinal);
        }

        builder.Append(content.TrimEnd('\n')).Append('\n');

        var orphanImages = noteImages.Where(image => !referencedImageIds.Contains(image.Id)).ToList();
        if (orphanImages.Count > 0)
        {
            builder.Append("\n## Attachments\n\n");
            foreach (var image in orphanImages)
            {
                var relativePath = upPrefix + AttachmentsFolder + "/" + EncodeMarkdownLinkPath(imagePlans[image.Id]);
                builder.Append("- ![").Append(imagePlans[image.Id]).Append("](").Append(relativePath).Append(")\n");
            }
        }

        if (outgoing.Count > 0 || incoming.Count > 0)
        {
            builder.Append("\n## Related notes\n\n");
            foreach (var relationship in outgoing)
            {
                if (!notePlans.TryGetValue(relationship.TargetNoteId, out var targetPlan))
                    continue;

                builder.Append("- ").Append(WikiLink(targetPlan)).Append(" — ")
                    .Append(DescribeOutgoing(relationship.Type)).Append('\n');
            }

            foreach (var relationship in incoming)
            {
                if (!notePlans.TryGetValue(relationship.SourceNoteId, out var sourcePlan))
                    continue;

                builder.Append("- ").Append(WikiLink(sourcePlan)).Append(" — ")
                    .Append(DescribeIncoming(relationship.Type)).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string WikiLink(NotePlan plan)
        => string.Equals(plan.FileStem, plan.Title, StringComparison.Ordinal)
            ? $"[[{plan.FileStem}]]"
            : $"[[{plan.FileStem}|{plan.Title}]]";

    private static string EncodeMarkdownLinkPath(string relativePath)
        => relativePath.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);

    private static string DescribeOutgoing(RelationshipType type) => type switch
    {
        RelationshipType.Related => "related to",
        RelationshipType.References => "references",
        RelationshipType.FollowUp => "follow-up of",
        RelationshipType.Duplicate => "duplicate of",
        RelationshipType.Supports => "supports",
        RelationshipType.Contradicts => "contradicts",
        _ => "related to"
    };

    private static string DescribeIncoming(RelationshipType type) => type switch
    {
        RelationshipType.Related => "related to",
        RelationshipType.References => "referenced by",
        RelationshipType.FollowUp => "followed up by",
        RelationshipType.Duplicate => "duplicate of",
        RelationshipType.Supports => "supported by",
        RelationshipType.Contradicts => "contradicted by",
        _ => "related to"
    };

    private static string ResolveImageExtension(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension))
            return extension;

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/bmp" => ".bmp",
            _ => ".bin"
        };
    }

    private static void WriteReadme(ZipArchive archive, DateTime exportedAtUtc)
    {
        var readme = new StringBuilder();
        readme.Append("# Brainy vault export\n\n");
        readme.Append("Exported ").Append(exportedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append(".\n\n");
        readme.Append("Notes are organised into `Projects/`, `Areas/`, `Resources/` and `Archive/` ");
        readme.Append("folders (Brainy's PARA categories), each note carrying YAML front matter ");
        readme.Append("(tags, dates, status, provenance) and `[[wiki links]]` to related notes. ");
        readme.Append("Note images are copied into `attachments/` and referenced by relative path.\n\n");
        readme.Append("This is a read-only, human-readable copy of your data for use outside Brainy ");
        readme.Append("(for example, opening it as an Obsidian vault). It does not round-trip back ");
        readme.Append("into Brainy — use the JSON export for that.\n\n");
        readme.Append("## Security\n\n");
        readme.Append("This archive never contains your account credentials, application secrets, ");
        readme.Append("AI-provider (BYOK) keys, or analytics identifiers. User-authored content is ");
        readme.Append("exported verbatim; any secret a user chose to paste into a note or source URL ");
        readme.Append("remains in that content, exactly as in the JSON export.\n");

        WriteTextEntry(archive, "README.md", readme.ToString());
    }

    private static void WriteTextEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string entryPath, byte[] data)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(data, 0, data.Length);
    }

    private sealed record ContainerRow(Guid Id, string Name);

    private sealed record NoteRow(
        Guid Id,
        string Title,
        string Content,
        NoteStatus Status,
        bool IsArchived,
        DateTime? ArchivedAtUtc,
        ParaCategory ParaCategory,
        bool IsFavorite,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        Guid? ProjectId,
        Guid? AreaId,
        Guid? ResourceId,
        Guid? SourceId,
        SourceType? SourceType,
        string? SourceTitle,
        string? SourceUrl,
        string? SourceAuthor,
        DateTime? SourceCapturedAtUtc,
        List<string> Tags);

    private sealed record ImageRow(Guid Id, Guid NoteId, string FileName, string ContentType, byte[] Data);

    private sealed record RelationshipRow(Guid Id, Guid SourceNoteId, Guid TargetNoteId, RelationshipType Type);

    private sealed record NotePlan(string Title, string FolderPath, string FileStem);
}
