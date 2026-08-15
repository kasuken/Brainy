using System.Text;
using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

public sealed class DataImportServiceTests
{
    private const string UserId = "import-user";

    private static (IDataImportService Sut, BrainyDbContext Db) BuildService(
        string databaseName,
        ICurrentUserService? currentUser = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton(currentUser ?? new FakeCurrentUserService(UserId));
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDataImportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithUnsupportedSchema_Throws()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_WithUnsupportedSchema_Throws));
        var json = """
            {
              "schemaVersion": "2.0",
              "product": "Brainy",
              "data": {
                "tags": [],
                "notes": [],
                "noteTagLinks": []
              }
            }
            """;

        var act = () => sut.ImportCurrentUserAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*schema 1.0 files only*");
        (await db.Tags.CountAsync()).Should().Be(0);
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ImportsStandaloneNotesAndTagsAndReportsUnsupportedSections()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_ImportsStandaloneNotesAndTagsAndReportsUnsupportedSections));
        var existingTag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = "Existing",
            Color = "#000"
        };
        db.Tags.Add(existingTag);
        await db.SaveChangesAsync();

        var standaloneNoteId = Guid.NewGuid();
        var relationalNoteId = Guid.NewGuid();
        var existingTagExportId = Guid.NewGuid();
        var freshTagExportId = Guid.NewGuid();
        var missingTagExportId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var json = $$"""
            {
              "schemaVersion": "1.0",
              "product": "Brainy",
              "exportedAtUtc": "2026-08-15T00:00:00Z",
              "security": {
                "accountCredentialsIncluded": false,
                "applicationSecretsIncluded": false,
                "note": "test"
              },
              "images": {
                "encoding": "base64",
                "integrity": "sha256",
                "note": "test"
              },
              "data": {
                "areas": [],
                "projects": [ { "id": "{{projectId}}" } ],
                "resources": [],
                "sources": [],
                "notes": [
                  {
                    "id": "{{standaloneNoteId}}",
                    "title": "Standalone note",
                    "content": "Imported safely",
                    "aiSummary": "Short summary",
                    "status": "active",
                    "isArchived": false,
                    "archivedAtUtc": null,
                    "processedAtUtc": "2026-08-14T00:00:00Z",
                    "paraCategory": "resource",
                    "sourceId": null,
                    "projectId": null,
                    "areaId": null,
                    "resourceId": null,
                    "isFavorite": true,
                    "createdAtUtc": "2026-08-01T00:00:00Z",
                    "updatedAtUtc": "2026-08-02T00:00:00Z"
                  },
                  {
                    "id": "{{relationalNoteId}}",
                    "title": "Relational note",
                    "content": "Should be skipped",
                    "aiSummary": null,
                    "status": "active",
                    "isArchived": false,
                    "archivedAtUtc": null,
                    "processedAtUtc": null,
                    "paraCategory": "project",
                    "sourceId": null,
                    "projectId": "{{projectId}}",
                    "areaId": null,
                    "resourceId": null,
                    "isFavorite": false,
                    "createdAtUtc": "2026-08-03T00:00:00Z",
                    "updatedAtUtc": "2026-08-03T00:00:00Z"
                  }
                ],
                "tags": [
                  { "id": "{{existingTagExportId}}", "name": "Existing", "color": "#111" },
                  { "id": "{{freshTagExportId}}", "name": "Fresh", "color": "#222" }
                ],
                "noteTagLinks": [
                  { "noteId": "{{standaloneNoteId}}", "tagId": "{{existingTagExportId}}" },
                  { "noteId": "{{standaloneNoteId}}", "tagId": "{{freshTagExportId}}" },
                  { "noteId": "{{standaloneNoteId}}", "tagId": "{{missingTagExportId}}" },
                  { "noteId": "{{relationalNoteId}}", "tagId": "{{freshTagExportId}}" }
                ],
                "resourceTagLinks": [],
                "noteImages": [],
                "highlights": [],
                "summaries": [],
                "actionItems": [],
                "noteRelationships": [],
                "tasks": [ { "id": "{{Guid.NewGuid()}}" }, { "id": "{{Guid.NewGuid()}}" } ],
                "taskDependencies": [],
                "outputs": [],
                "outputSourceNoteLinks": [],
                "ideas": [],
                "goals": [],
                "goalMilestones": [],
                "goalActivities": [],
                "archiveRetentionRules": [],
                "dashboardPreferences": [],
                "lifecycleActivities": []
              }
            }
            """;

        var result = await sut.ImportCurrentUserAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        result.Should().BeEquivalentTo(new DataImportResultDto(
            "1.0",
            ImportedTags: 1,
            ReusedTags: 1,
            SkippedTags: 0,
            ImportedNotes: 1,
            SkippedNotes: 1,
            LinkedNoteTags: 2,
            SkippedNoteTagLinks: 2,
            UnsupportedEntities:
            [
                new DataImportEntityCountDto("Projects", 1),
                new DataImportEntityCountDto("Tasks", 2)
            ]));

        var importedTags = await db.Tags
            .AsNoTracking()
            .Where(tag => tag.UserId == UserId)
            .OrderBy(tag => tag.Name)
            .ToListAsync();
        importedTags.Select(tag => tag.Name).Should().Equal("Existing", "Fresh");

        var importedNote = await db.Notes
            .Include(note => note.Tags)
            .SingleAsync(note => note.UserId == UserId);
        importedNote.Title.Should().Be("Standalone note");
        importedNote.ProjectId.Should().BeNull();
        importedNote.Tags.Select(tag => tag.Name).Should().BeEquivalentTo(["Existing", "Fresh"]);
    }
}
