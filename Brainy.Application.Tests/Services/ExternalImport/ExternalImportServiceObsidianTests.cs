using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services.ExternalImport;

/// <summary>
/// Covers the Obsidian-vault and generic-Markdown-folder paths of
/// <see cref="IExternalImportService"/>: front matter/tags/[[wiki links]] preserved,
/// preview-without-writing, duplicate-safe re-import, per-user isolation, and a
/// round-trip against <c>MarkdownExportService</c>'s own output.
/// </summary>
public sealed class ExternalImportServiceObsidianTests
{
    private const string UserId = "obsidian-user";
    private const string OtherUserId = "other-vault-user";

    private static (IExternalImportService Sut, BrainyDbContext Db) BuildService(
        string databaseName, string userId = UserId, InMemoryDatabaseRoot? root = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options =>
        {
            if (root is null) options.UseInMemoryDatabase(databaseName);
            else options.UseInMemoryDatabase(databaseName, root);
        });
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IExternalImportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    private static MemoryStream Zip(IReadOnlyDictionary<string, string> entries) =>
        new(ExternalImportTestZipBuilder.Build(entries));

    [Fact]
    public async Task PreviewImportAsync_ComputesThePlanWithoutWritingAnything()
    {
        var (sut, db) = BuildService(nameof(PreviewImportAsync_ComputesThePlanWithoutWritingAnything));
        var zip = Zip(new Dictionary<string, string>
        {
            ["Notes/Alpha.md"] = "---\ntags: [\"work\", \"idea\"]\n---\n\n# Alpha\n\nBody linking to [[Beta]].",
            ["Notes/Beta.md"] = "# Beta\n\nUnrelated body."
        });

        var preview = await sut.PreviewImportAsync(ExternalImportSourceFormat.ObsidianVault, zip);

        preview.SourceFormat.Should().Be("Obsidian vault");
        preview.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);
        preview.EntityOutcomes.Single(o => o.EntityType == "Tags").Created.Should().Be(2);
        preview.EntityOutcomes.Single(o => o.EntityType == "Note relationships").Created.Should().Be(1);

        (await db.Notes.CountAsync()).Should().Be(0);
        (await db.Tags.CountAsync()).Should().Be(0);
        (await db.NoteRelationships.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_PreservesFrontMatterTagsHashtagsAndWikiLinks()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_PreservesFrontMatterTagsHashtagsAndWikiLinks));
        var zip = Zip(new Dictionary<string, string>
        {
            ["Notes/Alpha.md"] = "---\ntags: [\"work\"]\n---\n\n# Alpha\n\nRelated to [[Beta]] and tagged #idea.",
            ["Notes/Beta.md"] = "# Beta\n\nSome unrelated body."
        });

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, zip);

        result.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);

        var alpha = await db.Notes.Include(n => n.Tags).SingleAsync(n => n.Title == "Alpha");
        alpha.Status.Should().Be(NoteStatus.Inbox);
        alpha.ParaCategory.Should().Be(ParaCategory.Project);
        alpha.Tags.Select(t => t.Name).Should().BeEquivalentTo(["work", "idea"]);

        var beta = await db.Notes.SingleAsync(n => n.Title == "Beta");
        beta.Status.Should().Be(NoteStatus.Inbox);

        var relationship = await db.NoteRelationships.SingleAsync();
        relationship.SourceNoteId.Should().Be(alpha.Id);
        relationship.TargetNoteId.Should().Be(beta.Id);
        relationship.Type.Should().Be(RelationshipType.Related);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ReimportingTheSameVault_IsANoOp()
    {
        var databaseName = nameof(ImportCurrentUserAsync_ReimportingTheSameVault_IsANoOp);
        var root = new InMemoryDatabaseRoot();
        var (sut, _) = BuildService(databaseName, root: root);

        var entries = new Dictionary<string, string>
        {
            ["Notes/Alpha.md"] = "---\ntags: [\"work\"]\n---\n\n# Alpha\n\nLinks to [[Beta]].",
            ["Notes/Beta.md"] = "# Beta\n\nBody."
        };

        var first = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, Zip(entries));
        first.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);

        var (sutAgain, db) = BuildService(databaseName, root: root);
        var second = await sutAgain.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, Zip(entries));

        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(0);
        second.EntityOutcomes.Single(o => o.EntityType == "Notes").Reused.Should().Be(2);
        second.EntityOutcomes.Single(o => o.EntityType == "Tags").Created.Should().Be(0);
        second.EntityOutcomes.Single(o => o.EntityType == "Note relationships").Created.Should().Be(0);

        (await db.Notes.CountAsync()).Should().Be(2);
        (await db.Tags.CountAsync()).Should().Be(1);
        (await db.NoteRelationships.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_IsScopedToTheImportingUserAndNeverMatchesAnotherAccount()
    {
        var databaseName = nameof(ImportCurrentUserAsync_IsScopedToTheImportingUserAndNeverMatchesAnotherAccount);
        var root = new InMemoryDatabaseRoot();
        var entries = new Dictionary<string, string> { ["Notes/Shared title.md"] = "# Shared title\n\nSame body." };

        var (ownerSut, ownerDb) = BuildService(databaseName, UserId, root);
        await ownerSut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, Zip(entries));

        var (otherSut, otherDb) = BuildService(databaseName, OtherUserId, root);
        var otherResult = await otherSut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, Zip(entries));

        otherResult.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(1);
        (await otherDb.Notes.CountAsync(n => n.UserId == OtherUserId)).Should().Be(1);
        (await ownerDb.Notes.CountAsync(n => n.UserId == UserId)).Should().Be(1);
        (await otherDb.Notes.CountAsync()).Should().Be(2); // both users' rows visible through the shared InMemory root
        (await otherDb.Notes.Where(n => n.UserId == OtherUserId).Select(n => n.Id).SingleAsync())
            .Should().NotBe(await ownerDb.Notes.Where(n => n.UserId == UserId).Select(n => n.Id).SingleAsync());
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithAmbiguousArchiveAndUnresolvedAttachment_ReportsConflictsAndIntegrityIssues()
    {
        var (sut, _) = BuildService(nameof(ImportCurrentUserAsync_WithAmbiguousArchiveAndUnresolvedAttachment_ReportsConflictsAndIntegrityIssues));
        var zip = Zip(new Dictionary<string, string>
        {
            ["Projects/Duplicate.md"] = "# Duplicate\n\nFirst copy.",
            ["Areas/Duplicate.md"] = "# Duplicate\n\nSecond copy referencing ![[missing.png]]."
        });

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, zip);

        result.Conflicts.Should().ContainSingle(c => c.Contains("Duplicate", StringComparison.Ordinal));
        result.IntegrityIssues.Should().ContainSingle(i => i.Contains("missing.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ImportsAnAttachmentAndRewritesItsReferenceToBrainysImageEndpoint()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_ImportsAnAttachmentAndRewritesItsReferenceToBrainysImageEndpoint));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        using var stream = new MemoryStream(ExternalImportTestZipBuilder.Build(
            new Dictionary<string, string> { ["Notes/Alpha.md"] = "# Alpha\n\n![[photo.png]]" },
            new Dictionary<string, byte[]> { ["Notes/photo.png"] = png }));

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, stream);

        result.EntityOutcomes.Single(o => o.EntityType == "Note images").Created.Should().Be(1);
        var image = await db.NoteImages.SingleAsync();
        image.Data.Should().BeEquivalentTo(png);
        image.ContentType.Should().Be("image/png");

        var note = await db.Notes.SingleAsync();
        note.Content.Should().Contain($"/api/note-images/{image.Id}");
        note.Content.Should().NotContain("[[photo.png]]");
    }

    [Fact]
    public async Task ImportCurrentUserAsync_GenericMarkdownFolder_ImportsHashtagsWithoutFrontMatter()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_GenericMarkdownFolder_ImportsHashtagsWithoutFrontMatter));
        var zip = Zip(new Dictionary<string, string>
        {
            ["plain-note.md"] = "# Plain note\n\nJust text with a #reading tag, no front matter."
        });

        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.MarkdownFolder, zip);

        result.SourceFormat.Should().Be("Markdown folder");
        var note = await db.Notes.Include(n => n.Tags).SingleAsync();
        note.Title.Should().Be("Plain note");
        note.Tags.Select(t => t.Name).Should().BeEquivalentTo(["reading"]);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ObsidianRoundTripsMarkdownExportServiceOutput()
    {
        var databaseName = nameof(ImportCurrentUserAsync_ObsidianRoundTripsMarkdownExportServiceOutput);
        var root = new InMemoryDatabaseRoot();
        const string sourceUserId = "export-source-user";

        var exportServices = new ServiceCollection();
        exportServices.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName, root));
        exportServices.AddScoped<IApplicationDbContext>(p => p.GetRequiredService<BrainyDbContext>());
        exportServices.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(sourceUserId));
        exportServices.AddSingleton<TimeProvider>(TimeProvider.System);
        exportServices.AddBrainyApplication();
        var exportProvider = exportServices.BuildServiceProvider();
        var exportDb = exportProvider.GetRequiredService<BrainyDbContext>();
        var exportService = exportProvider.GetRequiredService<IMarkdownExportService>();

        var project = new Project { Id = Guid.NewGuid(), UserId = sourceUserId, Name = "Launch" };
        var noteA = new Note
        {
            Id = Guid.NewGuid(), UserId = sourceUserId, Title = "Kickoff plan", Content = "Kickoff body.",
            ParaCategory = ParaCategory.Project, Project = project,
            Tags = [new Tag { Id = Guid.NewGuid(), UserId = sourceUserId, Name = "launch" }]
        };
        var noteB = new Note
        {
            Id = Guid.NewGuid(), UserId = sourceUserId, Title = "Follow-up notes", Content = "Follow-up body.",
            ParaCategory = ParaCategory.Project, Project = project
        };
        exportDb.AddRange(project, noteA, noteB);
        await exportDb.SaveChangesAsync();
        exportDb.NoteRelationships.Add(new NoteRelationship
        {
            Id = Guid.NewGuid(), SourceNoteId = noteA.Id, TargetNoteId = noteB.Id, Type = RelationshipType.Related
        });
        await exportDb.SaveChangesAsync();

        var vault = await exportService.ExportUserAsync(sourceUserId);

        var (sut, importDb) = BuildService(databaseName, root: root);
        var result = await sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, new MemoryStream(vault.Content));

        result.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);
        var importedA = await importDb.Notes.Include(n => n.Tags).SingleAsync(n => n.Title == "Kickoff plan" && n.UserId == UserId);
        var importedB = await importDb.Notes.SingleAsync(n => n.Title == "Follow-up notes" && n.UserId == UserId);
        importedA.Tags.Select(t => t.Name).Should().Contain("launch");
        importedA.Status.Should().Be(NoteStatus.Inbox);

        var relationship = await importDb.NoteRelationships
            .SingleAsync(r => r.SourceNoteId == importedA.Id && r.TargetNoteId == importedB.Id);
        relationship.Type.Should().Be(RelationshipType.Related);
    }
}
