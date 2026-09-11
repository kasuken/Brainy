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

/// <summary>
/// Unit tests for <see cref="IOfflineSnapshotService"/>, the Offline Lite (issue #302)
/// read-only Today/current-focus/favorites snapshot behind
/// <c>GET /api/offline/today-snapshot</c>. Resolved via the real DI container with an EF Core
/// InMemory database, mirroring <c>TaskServiceTests</c>'s direct-entity-seeding style.
/// </summary>
public class OfflineSnapshotServiceTests
{
    private const string DefaultUserId = "offline-snapshot-user-1";
    private const string OtherUserId = "offline-snapshot-user-2";

    private static (IOfflineSnapshotService Service, BrainyDbContext Db) BuildService(
        string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IOfflineSnapshotService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = "Snapshot Project",
        Status = ProjectStatus.Active,
        Priority = ProjectPriority.Medium,
    };

    private static TaskItem CreateTask(Guid projectId, string userId, bool isCurrent) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ProjectId = projectId,
        Title = "Ship the offline snapshot",
        Status = TaskItemStatus.InProgress,
        Priority = TaskPriority.Medium,
        IsCurrentTask = isCurrent,
    };

    private static Note CreateNote(string userId, string title, bool isFavorite = false, bool isArchived = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = title,
        Content = "content",
        Status = NoteStatus.Active,
        ParaCategory = ParaCategory.Project,
        IsFavorite = isFavorite,
        IsArchived = isArchived,
    };

    [Fact]
    public async Task GetSnapshotAsync_WithCurrentTaskSet_IncludesItAsCurrentFocus()
    {
        var (sut, db) = BuildService(nameof(GetSnapshotAsync_WithCurrentTaskSet_IncludesItAsCurrentFocus));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isCurrent: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.CurrentFocus.Should().NotBeNull();
        snapshot.CurrentFocus!.Id.Should().Be(task.Id);
        snapshot.CurrentFocus.Title.Should().Be("Ship the offline snapshot");
    }

    [Fact]
    public async Task GetSnapshotAsync_WithNoCurrentTask_ReturnsNullCurrentFocus()
    {
        var (sut, db) = BuildService(nameof(GetSnapshotAsync_WithNoCurrentTask_ReturnsNullCurrentFocus));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isCurrent: false);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.CurrentFocus.Should().BeNull();
    }

    [Fact]
    public async Task GetSnapshotAsync_IncludesFavoriteNotes()
    {
        var (sut, db) = BuildService(nameof(GetSnapshotAsync_IncludesFavoriteNotes));
        db.Notes.Add(CreateNote(DefaultUserId, "Favorited idea", isFavorite: true));
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.Notes.Should().ContainSingle(n => n.Title == "Favorited idea" && n.IsFavorite);
    }

    [Fact]
    public async Task GetSnapshotAsync_ExcludesArchivedNotes()
    {
        var (sut, db) = BuildService(nameof(GetSnapshotAsync_ExcludesArchivedNotes));
        db.Notes.Add(CreateNote(DefaultUserId, "Archived note", isFavorite: true, isArchived: true));
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotAsync_CapsNotesAtMaxNotes()
    {
        var dbName = nameof(GetSnapshotAsync_CapsNotesAtMaxNotes);
        var (sut, db) = BuildService(dbName);
        for (var i = 0; i < IOfflineSnapshotService.MaxNotes + 5; i++)
        {
            db.Notes.Add(CreateNote(DefaultUserId, $"Note {i}"));
        }
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.Notes.Should().HaveCount(IOfflineSnapshotService.MaxNotes);
    }

    [Fact]
    public async Task GetSnapshotAsync_IsScopedToTheCurrentUser()
    {
        var dbName = nameof(GetSnapshotAsync_IsScopedToTheCurrentUser);
        var (sutForA, db) = BuildService(dbName, DefaultUserId);
        db.Notes.Add(CreateNote(OtherUserId, "Someone else's favorite", isFavorite: true));
        var projectB = CreateProject(OtherUserId);
        db.Projects.Add(projectB);
        db.Tasks.Add(CreateTask(projectB.Id, OtherUserId, isCurrent: true));
        await db.SaveChangesAsync();

        var snapshot = await sutForA.GetSnapshotAsync();

        snapshot.CurrentFocus.Should().BeNull();
        snapshot.Notes.Should().BeEmpty();
    }
}
