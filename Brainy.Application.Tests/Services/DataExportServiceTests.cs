using System.Text;
using System.Text.Json;
using Brainy.Application.Interfaces.Identity;
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

namespace Brainy.Application.Tests.Services;

public class DataExportServiceTests
{
    private const string DefaultUserId = "export-user";
    private const string OtherUserId = "foreign-user";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 13, 10, 11, 12, TimeSpan.Zero);

    private static (IDataExportService Sut, BrainyDbContext Db) BuildService(
        string databaseName,
        ICurrentUserService? currentUser = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton(currentUser ?? new FakeCurrentUserService(DefaultUserId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDataExportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ExportCurrentUserAsync_WithoutAuthenticatedUser_IsRejected()
    {
        var (sut, _) = BuildService(
            nameof(ExportCurrentUserAsync_WithoutAuthenticatedUser_IsRejected),
            new UnauthenticatedCurrentUserService());

        var act = () => sut.ExportCurrentUserAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ExportCurrentUserAsync_ExportsOnlyCurrentTenantAndEncodesImagesSafely()
    {
        var (sut, db) = BuildService(nameof(ExportCurrentUserAsync_ExportsOnlyCurrentTenantAndEncodesImagesSafely));
        var mine = await SeedTenantGraphAsync(db, DefaultUserId, "MINE");
        var foreign = await SeedTenantGraphAsync(db, OtherUserId, "FOREIGN-SECRET");

        var export = await sut.ExportCurrentUserAsync();

        export.SchemaVersion.Should().Be(IDataExportService.SchemaVersion);
        export.ContentType.Should().Be("application/json;charset=utf-8");
        export.FileName.Should().Be("brainy-data-export-20260813-101112Z-v1.0.json");

        var json = Encoding.UTF8.GetString(export.Content);
        json.Should().Contain("MINE");
        json.Should().NotContain("FOREIGN-SECRET");
        json.Should().NotContain(foreign.NoteId.ToString());
        json.Should().NotContain(foreign.ImageId.ToString());
        json.Should().NotContain("\"userId\"");
        json.Should().NotContain("passwordHash");
        json.Should().NotContain("securityStamp");
        json.Should().NotContain("rowVersion");

        using var document = JsonDocument.Parse(export.Content);
        var data = document.RootElement.GetProperty("data");
        var notes = data.GetProperty("notes").EnumerateArray().ToList();
        notes.Should().HaveCount(2);
        notes.Should().OnlyContain(note => note.GetProperty("title").GetString()!.StartsWith("MINE", StringComparison.Ordinal));

        var images = data.GetProperty("noteImages").EnumerateArray().ToList();
        var image = images.Should().ContainSingle().Which;
        image.GetProperty("id").GetGuid().Should().Be(mine.ImageId);
        image.GetProperty("fileName").GetString().Should().Be("MINE-image.png");
        image.GetProperty("dataBase64").GetString().Should().Be(Convert.ToBase64String(mine.ImageBytes));
        image.GetProperty("sha256").GetString().Should().MatchRegex("^[0-9a-f]{64}$");

        data.GetProperty("noteRelationships").GetArrayLength().Should().Be(1);
        data.GetProperty("taskDependencies").GetArrayLength().Should().Be(1);
        data.GetProperty("noteTagLinks").GetArrayLength().Should().Be(1);
        data.GetProperty("resourceTagLinks").GetArrayLength().Should().Be(1);
        data.GetProperty("outputSourceNoteLinks").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExportCurrentUserAsync_ProducesStableVersionOneSchema()
    {
        var (sut, db) = BuildService(nameof(ExportCurrentUserAsync_ProducesStableVersionOneSchema));
        await SeedTenantGraphAsync(db, DefaultUserId, "SCHEMA");

        var export = await sut.ExportCurrentUserAsync();

        using var document = JsonDocument.Parse(export.Content);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal(
            "schemaVersion",
            "product",
            "exportedAtUtc",
            "security",
            "images",
            "data");
        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        var security = root.GetProperty("security");
        security.EnumerateObject().Select(property => property.Name).Should().Equal(
            "accountCredentialsIncluded",
            "applicationSecretsIncluded",
            "note");
        security.GetProperty("accountCredentialsIncluded").GetBoolean().Should().BeFalse();
        security.GetProperty("applicationSecretsIncluded").GetBoolean().Should().BeFalse();
        root.GetProperty("images").GetProperty("encoding").GetString().Should().Be("base64");
        root.GetProperty("images").GetProperty("integrity").GetString().Should().Be("sha256");

        var data = root.GetProperty("data");
        data.EnumerateObject().Select(property => property.Name).Should().Equal(
            "areas",
            "projects",
            "resources",
            "sources",
            "notes",
            "tags",
            "noteTagLinks",
            "resourceTagLinks",
            "noteImages",
            "highlights",
            "summaries",
            "actionItems",
            "noteRelationships",
            "tasks",
            "taskDependencies",
            "outputs",
            "outputSourceNoteLinks",
            "ideas",
            "goals",
            "goalMilestones",
            "goalActivities",
            "archiveRetentionRules",
            "dashboardPreferences",
            "lifecycleActivities");

        data.GetProperty("notes")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "id",
            "title",
            "content",
            "aiSummary",
            "status",
            "isArchived",
            "archivedAtUtc",
            "processedAtUtc",
            "paraCategory",
            "sourceId",
            "projectId",
            "areaId",
            "resourceId",
            "isFavorite",
            "createdAtUtc",
            "updatedAtUtc");

        data.GetProperty("noteImages")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "id",
            "noteId",
            "fileName",
            "contentType",
            "sizeBytes",
            "sha256",
            "dataBase64",
            "createdAtUtc",
            "updatedAtUtc");
    }

    private static async Task<SeededTenant> SeedTenantGraphAsync(
        BrainyDbContext db,
        string userId,
        string marker)
    {
        var area = new Area { Id = Guid.NewGuid(), UserId = userId, Name = $"{marker} area" };
        var goal = new Goal { Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} goal", Area = area };
        var project = new Project
        {
            Id = Guid.NewGuid(), UserId = userId, Name = $"{marker} project", Area = area, Goal = goal
        };
        var resource = new Resource
        {
            Id = Guid.NewGuid(), UserId = userId, Name = $"{marker} resource", Area = area
        };
        var source = new Source
        {
            Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} source", Type = SourceType.Url,
            Url = $"https://example.test/{marker}"
        };
        var note = new Note
        {
            Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} note", Content = $"{marker} content",
            Status = NoteStatus.Active, ParaCategory = ParaCategory.Project, Area = area, Project = project,
            Resource = resource, Source = source
        };
        var relatedNote = new Note
        {
            Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} related note", Content = marker,
            Status = NoteStatus.Distilled, ParaCategory = ParaCategory.Resource
        };
        var tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Name = $"{marker} tag" };
        note.Tags.Add(tag);
        resource.Tags.Add(tag);

        var firstTask = new TaskItem
        {
            Id = Guid.NewGuid(), UserId = userId, Project = project, Title = $"{marker} first task",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium
        };
        var secondTask = new TaskItem
        {
            Id = Guid.NewGuid(), UserId = userId, Project = project, Title = $"{marker} second task",
            Status = TaskItemStatus.Todo, Priority = TaskPriority.Low
        };
        var output = new Output
        {
            Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} output", Content = marker,
            Type = OutputType.Report, Status = OutputStatus.Draft, Project = project, Area = area, Goal = goal
        };
        output.SourceNotes.Add(note);

        var imageBytes = Encoding.UTF8.GetBytes($"{marker}-image-bytes");
        var image = new NoteImage
        {
            Id = Guid.NewGuid(), UserId = userId, Note = note, FileName = $"../{marker}-image.png",
            ContentType = "image/png", SizeBytes = imageBytes.Length, Data = imageBytes
        };

        db.AddRange(
            area,
            goal,
            project,
            resource,
            source,
            note,
            relatedNote,
            tag,
            firstTask,
            secondTask,
            output,
            image,
            new Highlight { Id = Guid.NewGuid(), Note = note, Text = $"{marker} highlight" },
            new Summary { Id = Guid.NewGuid(), Note = note, Content = $"{marker} summary" },
            new ActionItem { Id = Guid.NewGuid(), Note = note, Title = $"{marker} action" },
            new NoteRelationship
            {
                Id = Guid.NewGuid(), SourceNote = note, TargetNote = relatedNote, Type = RelationshipType.Related
            },
            new TaskDependency
            {
                Id = Guid.NewGuid(), Task = secondTask, DependsOnTask = firstTask
            },
            new Idea { Id = Guid.NewGuid(), UserId = userId, Title = $"{marker} idea", Area = area },
            new GoalMilestone { Id = Guid.NewGuid(), Goal = goal, Title = $"{marker} milestone" },
            new GoalActivity
            {
                Id = Guid.NewGuid(), Goal = goal, Description = $"{marker} goal activity"
            },
            new ArchiveRetentionRule
            {
                Id = Guid.NewGuid(), UserId = userId, EntityType = $"{marker} notes", RetentionDays = 30
            },
            new UserDashboardPreference
            {
                Id = Guid.NewGuid(), UserId = userId, WidgetOrder = $"{marker} widget"
            },
            new LifecycleActivity
            {
                Id = Guid.NewGuid(), UserId = userId, EntityId = note.Id,
                ActivityType = PulseActivityType.NoteCaptured, OccurredAtUtc = FixedNow.UtcDateTime,
                Title = $"{marker} lifecycle"
            });

        await db.SaveChangesAsync();
        return new SeededTenant(note.Id, image.Id, imageBytes);
    }

    private sealed record SeededTenant(Guid NoteId, Guid ImageId, byte[] ImageBytes);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class UnauthenticatedCurrentUserService : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default)
            => Task.FromException<string>(new UnauthorizedAccessException());
    }
}
