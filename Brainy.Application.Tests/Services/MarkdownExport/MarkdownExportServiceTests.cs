using System.IO.Compression;
using System.Text;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services.MarkdownExport;

public class MarkdownExportServiceTests
{
    private const string DefaultUserId = "vault-user";
    private const string OtherUserId = "foreign-vault-user";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 13, 10, 11, 12, TimeSpan.Zero);

    private static (IMarkdownExportService Sut, BrainyDbContext Db, FixedTimeProvider Clock) BuildService(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton(new FakeCurrentUserService(DefaultUserId));
        var clock = new FixedTimeProvider(FixedNow);
        services.AddSingleton<TimeProvider>(clock);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IMarkdownExportService>(),
            provider.GetRequiredService<BrainyDbContext>(),
            clock);
    }

    private static Dictionary<string, string> ReadTextEntries(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".md", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }

    private static byte[] ReadBinaryEntry(byte[] zipBytes, string entryPath)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryPath) ?? throw new InvalidOperationException($"Entry '{entryPath}' not found.");
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task ExportUserAsync_OnlyIncludesTheRequestedUsersData()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_OnlyIncludesTheRequestedUsersData));

        var mineArea = new Area { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Mine area" };
        var mineProject = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Mine project" };
        var mineNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Mine note", Content = "mine-secret-content",
            ParaCategory = ParaCategory.Project, Project = mineProject
        };

        var foreignProject = new Project { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Foreign project" };
        var foreignNote = new Note
        {
            Id = Guid.NewGuid(), UserId = OtherUserId, Title = "Foreign note", Content = "foreign-secret-content",
            ParaCategory = ParaCategory.Project, Project = foreignProject
        };

        db.AddRange(mineArea, mineProject, mineNote, foreignProject, foreignNote);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);

        var entries = ReadTextEntries(export.Content);
        entries.Keys.Should().Contain("Projects/Mine project/Mine note.md");
        entries.Keys.Should().NotContain(path => path.Contains("Foreign", StringComparison.Ordinal));

        var allContent = string.Join("\n", entries.Values);
        allContent.Should().Contain("mine-secret-content");
        allContent.Should().NotContain("foreign-secret-content");
        allContent.Should().NotContain(foreignNote.Id.ToString());
    }

    [Fact]
    public async Task ExportUserAsync_OrganisesNotesIntoParaCategoryFolders()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_OrganisesNotesIntoParaCategoryFolders));

        var project = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Q3 launch" };
        var area = new Area { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Health" };
        var resource = new Resource { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Cooking" };

        var projectNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Kickoff notes", Content = "kickoff",
            ParaCategory = ParaCategory.Project, Project = project
        };
        var areaNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Sleep routine", Content = "sleep",
            ParaCategory = ParaCategory.Area, Area = area
        };
        var resourceNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Pasta recipe", Content = "pasta",
            ParaCategory = ParaCategory.Resource, Resource = resource
        };
        var archivedNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Old idea", Content = "old",
            ParaCategory = ParaCategory.Archive, IsArchived = true, ArchivedAtUtc = FixedNow.UtcDateTime
        };
        var unfiledNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Loose thought", Content = "loose",
            ParaCategory = ParaCategory.Project
        };

        db.AddRange(project, area, resource, projectNote, areaNote, resourceNote, archivedNote, unfiledNote);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);

        entries.Keys.Should().Contain("Projects/Q3 launch/Kickoff notes.md");
        entries.Keys.Should().Contain("Areas/Health/Sleep routine.md");
        entries.Keys.Should().Contain("Resources/Cooking/Pasta recipe.md");
        entries.Keys.Should().Contain("Archive/Old idea.md");
        entries.Keys.Should().Contain("Projects/Unfiled/Loose thought.md");
    }

    [Fact]
    public async Task ExportUserAsync_WritesYamlFrontMatterWithTagsDatesStatusAndProvenance()
    {
        var (sut, db, clock) = BuildService(nameof(ExportUserAsync_WritesYamlFrontMatterWithTagsDatesStatusAndProvenance));

        var source = new Source
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Type = SourceType.Url,
            Title = "Original article", Url = "https://example.test/article"
        };
        var tag = new Tag { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "focus" };
        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Provenance note", Content = "body text",
            ParaCategory = ParaCategory.Resource, Status = NoteStatus.Distilled, Source = source,
            IsFavorite = true
        };
        note.Tags.Add(tag);

        db.AddRange(source, tag, note);
        await db.SaveChangesAsync(); // stamps CreatedAtUtc = UpdatedAtUtc = FixedNow

        clock.Advance(TimeSpan.FromDays(1));
        db.Entry(note).State = EntityState.Modified;
        await db.SaveChangesAsync(); // stamps UpdatedAtUtc only, one day later

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);
        var markdown = entries.Single(e => e.Key.EndsWith("Provenance note.md", StringComparison.Ordinal)).Value;

        markdown.Should().StartWith("---\n");
        markdown.Should().Contain("title: \"Provenance note\"");
        markdown.Should().Contain($"brainy_id: \"{note.Id}\"");
        markdown.Should().Contain("para: \"resource\"");
        markdown.Should().Contain("status: \"distilled\"");
        markdown.Should().Contain("favorite: true");
        markdown.Should().Contain("archived: false");
        markdown.Should().Contain("created: \"2026-08-13T10:11:12Z\"");
        markdown.Should().Contain("updated: \"2026-08-14T10:11:12Z\"");
        markdown.Should().Contain("tags: [\"focus\"]");
        markdown.Should().Contain("source:");
        markdown.Should().Contain("  type: \"url\"");
        markdown.Should().Contain("  title: \"Original article\"");
        markdown.Should().Contain("  url: \"https://example.test/article\"");
        markdown.Should().Contain("# Provenance note");
        markdown.Should().Contain("body text");
    }

    [Fact]
    public async Task ExportUserAsync_EscapesQuotesAndBackslashesInFrontMatterStrings()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_EscapesQuotesAndBackslashesInFrontMatterStrings));

        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "A \"quoted\" \\ title", Content = "content",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        db.Add(note);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);
        var markdown = entries.Single(e => e.Key.StartsWith("Archive/", StringComparison.Ordinal)).Value;

        markdown.Should().Contain("title: \"A \\\"quoted\\\" \\\\ title\"");
    }

    [Fact]
    public async Task ExportUserAsync_ProducesWikiLinksForNoteRelationshipsInBothDirections()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_ProducesWikiLinksForNoteRelationshipsInBothDirections));

        var source = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Source note", Content = "source",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        var target = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Target note", Content = "target",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        var relationship = new NoteRelationship
        {
            Id = Guid.NewGuid(), SourceNote = source, TargetNote = target, Type = RelationshipType.References
        };

        db.AddRange(source, target, relationship);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);

        var sourceMarkdown = entries.Single(e => e.Key.EndsWith("Source note.md", StringComparison.Ordinal)).Value;
        var targetMarkdown = entries.Single(e => e.Key.EndsWith("Target note.md", StringComparison.Ordinal)).Value;

        sourceMarkdown.Should().Contain("## Related notes");
        sourceMarkdown.Should().Contain("[[Target note]] — references");
        targetMarkdown.Should().Contain("[[Source note]] — referenced by");
    }

    [Fact]
    public async Task ExportUserAsync_ExportsImagesAndRewritesContentToRelativePaths()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_ExportsImagesAndRewritesContentToRelativePaths));

        var project = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Photo project" };
        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Note with image",
            ParaCategory = ParaCategory.Project, Project = project
        };
        var imageBytes = Encoding.UTF8.GetBytes("fake-png-bytes");
        var image = new NoteImage
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Note = note, FileName = "photo.png",
            ContentType = "image/png", SizeBytes = imageBytes.Length, Data = imageBytes
        };
        note.Content = $"Before\n![photo]({{0}})\nAfter".Replace("{0}", $"/api/note-images/{image.Id}");

        db.AddRange(project, note, image);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);
        var markdown = entries.Single(e => e.Key.EndsWith("Note with image.md", StringComparison.Ordinal)).Value;

        markdown.Should().NotContain("/api/note-images/");
        markdown.Should().Contain("../../attachments/photo.png");

        var storedBytes = ReadBinaryEntry(export.Content, "attachments/photo.png");
        storedBytes.Should().Equal(imageBytes);
    }

    [Fact]
    public async Task ExportUserAsync_ListsUnreferencedImagesAsAttachments()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_ListsUnreferencedImagesAsAttachments));

        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Orphan image note", Content = "no image markup here",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        var imageBytes = Encoding.UTF8.GetBytes("orphan-bytes");
        var image = new NoteImage
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Note = note, FileName = "orphan.png",
            ContentType = "image/png", SizeBytes = imageBytes.Length, Data = imageBytes
        };

        db.AddRange(note, image);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);
        var markdown = entries.Single(e => e.Key.EndsWith("Orphan image note.md", StringComparison.Ordinal)).Value;

        markdown.Should().Contain("## Attachments");
        markdown.Should().Contain("../attachments/orphan.png");
    }

    [Fact]
    public async Task ExportUserAsync_ResolvesDuplicateTitlesWithoutOverwriting()
    {
        var (sut, db, clock) = BuildService(nameof(ExportUserAsync_ResolvesDuplicateTitlesWithoutOverwriting));

        // Two separate saves, with the clock advanced between them, so CreatedAtUtc (stamped
        // by BrainyDbContext on save) genuinely orders "first" before "second" — the export
        // allocates file names in creation order, so this pins down which one keeps the bare
        // title and which one gets the " (2)" suffix.
        var first = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Weekly review", Content = "first-content",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        db.Add(first);
        await db.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(1));
        var second = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Weekly review", Content = "second-content",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        db.Add(second);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);

        entries.Keys.Should().Contain("Archive/Weekly review.md");
        entries.Keys.Should().Contain("Archive/Weekly review (2).md");

        entries["Archive/Weekly review.md"].Should().Contain("first-content");
        entries["Archive/Weekly review (2).md"].Should().Contain("second-content");
    }

    [Fact]
    public async Task ExportUserAsync_SanitizesReservedCharactersAndDeviceNamesInTitles()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_SanitizesReservedCharactersAndDeviceNamesInTitles));

        var slashNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Q1/Q2 planning", Content = "slash",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        var deviceNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "CON", Content = "device",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };
        var emptyNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "///", Content = "empty-after-sanitizing",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        };

        db.AddRange(slashNote, deviceNote, emptyNote);
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);
        var entries = ReadTextEntries(export.Content);

        entries.Keys.Should().Contain("Archive/Q1 Q2 planning.md");
        entries.Keys.Should().Contain(path => path.StartsWith("Archive/CON", StringComparison.Ordinal) && path != "Archive/CON.md");
        entries.Keys.Should().Contain("Archive/untitled.md");
    }

    [Fact]
    public async Task ExportUserAsync_IncludesAReadmeWithASecurityAssurance()
    {
        var (sut, db, _) = BuildService(nameof(ExportUserAsync_IncludesAReadmeWithASecurityAssurance));
        db.Add(new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Any note", Content = "content",
            ParaCategory = ParaCategory.Archive, IsArchived = true
        });
        await db.SaveChangesAsync();

        var export = await sut.ExportUserAsync(DefaultUserId);

        export.FileName.Should().MatchRegex(@"^brainy-vault-export-\d{8}-\d{6}Z\.zip$");
        export.ContentType.Should().Be("application/zip");

        using var stream = new MemoryStream(export.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var readme = archive.GetEntry("README.md");
        readme.Should().NotBeNull();

        using var reader = new StreamReader(readme!.Open(), Encoding.UTF8);
        var readmeText = reader.ReadToEnd();
        readmeText.Should().Contain("does not round-trip back");
        readmeText.Should().Contain("never contains your account credentials");
        readmeText.Should().Contain("BYOK");
    }

    [Fact]
    public async Task ExportUserAsync_ThrowsForBlankUserId()
    {
        var (sut, _, _) = BuildService(nameof(ExportUserAsync_ThrowsForBlankUserId));

        var act = () => sut.ExportUserAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
