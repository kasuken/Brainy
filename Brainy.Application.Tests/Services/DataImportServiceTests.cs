using System.Text;
using Brainy.Application.Analytics;
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

namespace Brainy.Application.Tests.Services;

public sealed class DataImportServiceTests
{
    private const string UserId = "import-user";

    private static (IDataImportService Sut, BrainyDbContext Db) BuildService(
        string databaseName,
        ICurrentUserService? currentUser = null,
        InMemoryDatabaseRoot? root = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options =>
        {
            if (root is null) options.UseInMemoryDatabase(databaseName);
            else options.UseInMemoryDatabase(databaseName, root);
        });
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton(currentUser ?? new FakeCurrentUserService(UserId));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDataImportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    private static (IDataExportService Sut, BrainyDbContext Db) BuildExportService(
        string databaseName, InMemoryDatabaseRoot root, string userId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName, root));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDataExportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithUnsupportedSchema_Throws()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_WithUnsupportedSchema_Throws));
        var json = BuildEmptyExportJson(schemaVersion: "99.0");

        var act = () => sut.ImportCurrentUserAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*schema {IDataExportService.SchemaVersion} files only*");
        (await db.Areas.CountAsync()).Should().Be(0);
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PreviewImportAsync_ComputesPlanWithoutWritingToTheDatabase()
    {
        var databaseName = nameof(PreviewImportAsync_ComputesPlanWithoutWritingToTheDatabase);
        var root = new InMemoryDatabaseRoot();
        var sourceUserId = "preview-source-user";

        var (exportSut, sourceDb) = BuildExportService(databaseName, root, sourceUserId);
        await SeedRichAccountAsync(sourceDb, sourceUserId);
        var export = await exportSut.ExportCurrentUserAsync();

        var (importSut, targetDb) = BuildService(databaseName, new FakeCurrentUserService(UserId), root);

        var preview = await importSut.PreviewImportAsync(new MemoryStream(export.Content));

        preview.SchemaVersion.Should().Be(IDataExportService.SchemaVersion);
        preview.EntityOutcomes.Should().Contain(o => o.EntityType == "Notes" && o.Created >= 1);
        preview.EntityOutcomes.Should().Contain(o => o.EntityType == "Areas" && o.Created >= 1);

        (await targetDb.Areas.CountAsync(a => a.UserId == UserId)).Should().Be(0);
        (await targetDb.Notes.CountAsync(n => n.UserId == UserId)).Should().Be(0);
        (await targetDb.Tags.CountAsync(t => t.UserId == UserId)).Should().Be(0);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_FullRoundTrip_RestoresEntitiesRelationshipsAndArchiveStateIntoCleanAccount()
    {
        var databaseName = nameof(ImportCurrentUserAsync_FullRoundTrip_RestoresEntitiesRelationshipsAndArchiveStateIntoCleanAccount);
        var root = new InMemoryDatabaseRoot();
        const string sourceUserId = "roundtrip-source-user";
        const string targetUserId = "roundtrip-target-user";

        var (exportSut, sourceDb) = BuildExportService(databaseName, root, sourceUserId);
        var seed = await SeedRichAccountAsync(sourceDb, sourceUserId);
        var export = await exportSut.ExportCurrentUserAsync();

        var (importSut, targetDb) = BuildService(databaseName, new FakeCurrentUserService(targetUserId), root);
        var result = await importSut.ImportCurrentUserAsync(new MemoryStream(export.Content));

        result.SchemaVersion.Should().Be(IDataExportService.SchemaVersion);
        result.UnsupportedEntities.Should().Contain(e => e.EntityType == "Dashboard preferences");
        result.UnsupportedEntities.Should().Contain(e => e.EntityType == "Lifecycle activities");
        result.UnsupportedEntities.Should().Contain(e => e.EntityType == "Analytics events");

        var importedArea = await targetDb.Areas.AsNoTracking().SingleAsync(a => a.UserId == targetUserId);
        importedArea.Name.Should().Be(seed.AreaName);

        var importedProject = await targetDb.Projects.AsNoTracking().SingleAsync(p => p.UserId == targetUserId);
        importedProject.Name.Should().Be(seed.ProjectName);
        importedProject.AreaId.Should().Be(importedArea.Id);

        var importedNote = await targetDb.Notes.AsNoTracking().Include(n => n.Tags)
            .SingleAsync(n => n.UserId == targetUserId && n.Title == seed.NoteTitle);
        importedNote.ProjectId.Should().Be(importedProject.Id);
        importedNote.AreaId.Should().Be(importedArea.Id);
        importedNote.Tags.Select(t => t.Name).Should().Contain(seed.TagName);

        var importedTask = await targetDb.Tasks.AsNoTracking().SingleAsync(t => t.UserId == targetUserId);
        importedTask.ProjectId.Should().Be(importedProject.Id);
        importedTask.IsArchived.Should().BeTrue();
        importedTask.ArchivedAtUtc.Should().NotBeNull();
        importedTask.IsCurrentTask.Should().BeFalse();

        var importedImage = await targetDb.NoteImages.AsNoTracking().SingleAsync(i => i.UserId == targetUserId);
        importedImage.NoteId.Should().Be(importedNote.Id);
        importedImage.Data.Should().BeEquivalentTo(seed.ImageBytes);

        var importedGoal = await targetDb.Goals.AsNoTracking().SingleAsync(g => g.UserId == targetUserId);
        var importedMilestone = await targetDb.GoalMilestones.AsNoTracking().SingleAsync(m => m.GoalId == importedGoal.Id);
        importedMilestone.Title.Should().Be(seed.MilestoneTitle);

        var importedOutput = await targetDb.Outputs.AsNoTracking().Include(o => o.SourceNotes)
            .SingleAsync(o => o.UserId == targetUserId);
        importedOutput.SourceNotes.Select(n => n.Id).Should().Contain(importedNote.Id);

        var importedRule = await targetDb.ArchiveRetentionRules.AsNoTracking().SingleAsync(r => r.UserId == targetUserId);
        importedRule.EntityType.Should().Be("Note");
        importedRule.RetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_ReimportingTheSameExport_DoesNotDuplicateData()
    {
        var databaseName = nameof(ImportCurrentUserAsync_ReimportingTheSameExport_DoesNotDuplicateData);
        var root = new InMemoryDatabaseRoot();
        const string sourceUserId = "duplicate-source-user";
        const string targetUserId = "duplicate-target-user";

        var (exportSut, sourceDb) = BuildExportService(databaseName, root, sourceUserId);
        await SeedRichAccountAsync(sourceDb, sourceUserId);
        var export = await exportSut.ExportCurrentUserAsync();

        var (importSut, targetDb) = BuildService(databaseName, new FakeCurrentUserService(targetUserId), root);

        var firstImport = await importSut.ImportCurrentUserAsync(new MemoryStream(export.Content));
        var countsAfterFirst = await CountEntitiesAsync(targetDb, targetUserId);

        var secondImport = await importSut.ImportCurrentUserAsync(new MemoryStream(export.Content));
        var countsAfterSecond = await CountEntitiesAsync(targetDb, targetUserId);

        firstImport.EntityOutcomes.Should().Contain(o => o.EntityType == "Notes" && o.Created >= 1);
        secondImport.EntityOutcomes.Should().Contain(o => o.EntityType == "Notes" && o.Created == 0 && o.Reused >= 1);
        secondImport.EntityOutcomes.Should().Contain(o => o.EntityType == "Areas" && o.Created == 0 && o.Reused >= 1);

        countsAfterSecond.Should().BeEquivalentTo(countsAfterFirst);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_IsScopedToTheImportingUserAndNeverTouchesOtherAccounts()
    {
        var databaseName = nameof(ImportCurrentUserAsync_IsScopedToTheImportingUserAndNeverTouchesOtherAccounts);
        var root = new InMemoryDatabaseRoot();
        const string ownerUserId = "isolation-owner-user";
        const string importingUserId = "isolation-importing-user";

        var (exportSut, ownerDb) = BuildExportService(databaseName, root, ownerUserId);
        await SeedRichAccountAsync(ownerDb, ownerUserId);
        var export = await exportSut.ExportCurrentUserAsync();

        var ownerNoteCountBefore = await ownerDb.Notes.CountAsync(n => n.UserId == ownerUserId);
        var ownerAreaCountBefore = await ownerDb.Areas.CountAsync(a => a.UserId == ownerUserId);

        var (importSut, importingDb) = BuildService(databaseName, new FakeCurrentUserService(importingUserId), root);
        var result = await importSut.ImportCurrentUserAsync(new MemoryStream(export.Content));

        result.EntityOutcomes.Should().Contain(o => o.EntityType == "Notes" && o.Created >= 1);

        // The import must not create, modify, or attach to any row owned by the export's
        // original account: the owner's counts stay exactly as they were before the import.
        (await importingDb.Notes.CountAsync(n => n.UserId == ownerUserId)).Should().Be(ownerNoteCountBefore);
        (await importingDb.Areas.CountAsync(a => a.UserId == ownerUserId)).Should().Be(ownerAreaCountBefore);

        var importedNotes = await importingDb.Notes.AsNoTracking().Where(n => n.UserId == importingUserId).ToListAsync();
        importedNotes.Should().NotBeEmpty();
        importedNotes.Should().OnlyContain(n => n.UserId == importingUserId);

        (await ownerDb.Notes.CountAsync(n => n.UserId == ownerUserId)).Should().Be(ownerNoteCountBefore);
        (await ownerDb.Areas.CountAsync(a => a.UserId == ownerUserId)).Should().Be(ownerAreaCountBefore);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithMismatchedImageChecksum_ReportsIntegrityIssueButImportsActualBytes()
    {
        var (sut, db) = BuildService(nameof(ImportCurrentUserAsync_WithMismatchedImageChecksum_ReportsIntegrityIssueButImportsActualBytes));
        var noteId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var imageBytes = Encoding.UTF8.GetBytes("actual-bytes");
        var dataBase64 = Convert.ToBase64String(imageBytes);

        var json = $$"""
            {
              "schemaVersion": "{{IDataExportService.SchemaVersion}}",
              "product": "Brainy",
              "data": {
                "areas": [], "sources": [], "goals": [], "projects": [], "resources": [], "tags": [],
                "notes": [
                  {
                    "id": "{{noteId}}", "title": "Note with image", "content": "body",
                    "status": "active", "isArchived": false, "paraCategory": "archive",
                    "isFavorite": false
                  }
                ],
                "noteTagLinks": [], "resourceTagLinks": [],
                "noteImages": [
                  { "id": "{{imageId}}", "noteId": "{{noteId}}", "fileName": "a.png", "contentType": "image/png",
                    "sizeBytes": 999, "sha256": "0000000000000000000000000000000000000000000000000000000000000", "dataBase64": "{{dataBase64}}" }
                ],
                "highlights": [], "summaries": [],
                "tasks": [], "actionItems": [], "noteRelationships": [], "taskDependencies": [],
                "outputs": [], "outputSourceNoteLinks": [],
                "ideas": [], "goalMilestones": [], "goalActivities": [],
                "archiveRetentionRules": [], "weeklyTaskSelections": []
              }
            }
            """;

        var result = await sut.ImportCurrentUserAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        result.IntegrityIssues.Should().ContainSingle(issue => issue.Contains("checksum did not match", StringComparison.Ordinal));

        var importedImage = await db.NoteImages.AsNoTracking().SingleAsync();
        importedImage.Data.Should().BeEquivalentTo(imageBytes);
    }

    private static string BuildEmptyExportJson(string schemaVersion) => $$"""
        {
          "schemaVersion": "{{schemaVersion}}",
          "product": "Brainy",
          "data": {
            "areas": [], "sources": [], "goals": [], "projects": [], "resources": [], "tags": [],
            "notes": [], "noteTagLinks": [], "resourceTagLinks": [],
            "noteImages": [], "highlights": [], "summaries": [],
            "tasks": [], "actionItems": [], "noteRelationships": [], "taskDependencies": [],
            "outputs": [], "outputSourceNoteLinks": [],
            "ideas": [], "goalMilestones": [], "goalActivities": [],
            "archiveRetentionRules": [], "weeklyTaskSelections": []
          }
        }
        """;

    private static async Task<EntityCounts> CountEntitiesAsync(BrainyDbContext db, string userId) => new(
        await db.Areas.CountAsync(a => a.UserId == userId),
        await db.Projects.CountAsync(p => p.UserId == userId),
        await db.Resources.CountAsync(r => r.UserId == userId),
        await db.Notes.CountAsync(n => n.UserId == userId),
        await db.Tags.CountAsync(t => t.UserId == userId),
        await db.Tasks.CountAsync(t => t.UserId == userId),
        await db.NoteImages.CountAsync(i => i.UserId == userId),
        await db.Outputs.CountAsync(o => o.UserId == userId),
        await db.Goals.CountAsync(g => g.UserId == userId));

    private sealed record EntityCounts(
        int Areas, int Projects, int Resources, int Notes, int Tags, int Tasks, int NoteImages, int Outputs, int Goals);

    private sealed record SeededAccount(
        string AreaName, string ProjectName, string NoteTitle, string TagName, byte[] ImageBytes, string MilestoneTitle);

    private static async Task<SeededAccount> SeedRichAccountAsync(BrainyDbContext db, string userId)
    {
        var area = new Area { Id = Guid.NewGuid(), UserId = userId, Name = "Career" };
        var goal = new Goal { Id = Guid.NewGuid(), UserId = userId, Title = "Ship the book", Area = area };
        var project = new Project
        {
            Id = Guid.NewGuid(), UserId = userId, Name = "Write chapter one", Area = area, Goal = goal,
            Status = ProjectStatus.Active, Priority = ProjectPriority.High
        };
        var resource = new Resource { Id = Guid.NewGuid(), UserId = userId, Name = "Style guide", Area = area };
        var source = new Source
        {
            Id = Guid.NewGuid(), UserId = userId, Type = SourceType.Url, Title = "Reference article",
            Url = "https://example.test/article"
        };
        var tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Name = "writing" };
        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = userId, Title = "Draft outline", Content = "The outline body",
            Status = NoteStatus.Active, ParaCategory = ParaCategory.Project,
            Area = area, Project = project, Resource = resource, Source = source
        };
        note.Tags.Add(tag);

        var task = new TaskItem
        {
            Id = Guid.NewGuid(), UserId = userId, Project = project, Title = "Finish chapter one",
            Status = TaskItemStatus.Done, Priority = TaskPriority.High,
            IsArchived = true, ArchivedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsCurrentTask = true
        };

        var imageBytes = Encoding.UTF8.GetBytes("fake-image-bytes");
        var image = new NoteImage
        {
            Id = Guid.NewGuid(), UserId = userId, Note = note, FileName = "diagram.png",
            ContentType = "image/png", SizeBytes = imageBytes.Length, Data = imageBytes
        };

        var output = new Output
        {
            Id = Guid.NewGuid(), UserId = userId, Title = "Chapter one draft", Content = "Draft content",
            Type = OutputType.Report, Status = OutputStatus.Draft, Project = project
        };
        output.SourceNotes.Add(note);

        const string milestoneTitle = "First draft complete";
        var milestone = new GoalMilestone { Id = Guid.NewGuid(), Goal = goal, Title = milestoneTitle };
        var activity = new GoalActivity
        {
            Id = Guid.NewGuid(), Goal = goal, ActivityType = GoalActivityType.Created, Description = "Goal created"
        };

        var retentionRule = new ArchiveRetentionRule
        {
            Id = Guid.NewGuid(), UserId = userId, EntityType = "Note", RetentionDays = 30
        };

        var idea = new Idea { Id = Guid.NewGuid(), UserId = userId, Title = "Second book idea", Area = area };
        var highlight = new Highlight { Id = Guid.NewGuid(), Note = note, Text = "Key passage" };
        var summary = new Summary { Id = Guid.NewGuid(), Note = note, Content = "Short summary" };
        var actionItem = new ActionItem { Id = Guid.NewGuid(), UserId = userId, Note = note, Title = "Follow up", TaskItem = task };
        var dashboardPreference = new UserDashboardPreference { Id = Guid.NewGuid(), UserId = userId, WidgetOrder = "[\"CurrentTask\"]" };
        var productEvent = new ProductEvent
        {
            Id = Guid.NewGuid(), UserId = userId, EventName = AnalyticsEvents.CaptureCreated, OccurredAtUtc = DateTime.UtcNow
        };

        db.AddRange(
            area, goal, project, resource, source, tag, note, task, image, output,
            milestone, activity, retentionRule, idea, highlight, summary, actionItem,
            dashboardPreference, productEvent);

        await db.SaveChangesAsync();

        return new SeededAccount(area.Name, project.Name, note.Title, tag.Name, imageBytes, milestoneTitle);
    }
}
