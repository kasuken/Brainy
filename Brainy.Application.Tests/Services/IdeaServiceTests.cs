using Brainy.Application.DTOs.Ideas;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IIdeaService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Focuses on capture, archive lifecycle, and the three conversion flows.
/// </summary>
public class IdeaServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IIdeaService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IIdeaService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Idea CreateIdea(
        string userId,
        string title = "Idea",
        IdeaStatus status = IdeaStatus.Captured,
        bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, Status = status, IsArchived = isArchived };

    private static Project CreateProject(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "P" };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_PersistsCapturedIdeaForCurrentUser()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithValidDto_PersistsCapturedIdeaForCurrentUser));

        var result = await sut.CreateAsync(new CreateIdeaDto("  Newsletter tool  ", "desc", null));

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.UserId.Should().Be(DefaultUserId);
        stored.Title.Should().Be("Newsletter tool");
        stored.Status.Should().Be(IdeaStatus.Captured);
    }

    [Fact]
    public async Task CreateAsync_WithBlankTitle_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithBlankTitle_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateIdeaDto("   ", null, null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveAsync_ExcludesArchivedIdeas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesArchivedIdeas));
        db.Ideas.Add(CreateIdea(DefaultUserId, "Active"));
        db.Ideas.Add(CreateIdea(DefaultUserId, "Archived", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().ContainSingle()
            .Which.Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherUsersIdeas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesOtherUsersIdeas));
        db.Ideas.Add(CreateIdea(OtherUserId, "Foreign"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().BeEmpty();
    }

    // ── Archive / Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_SetsArchivedFlagAndTimestamp));
        var idea = CreateIdea(DefaultUserId);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(idea.Id);

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeTrue();
        stored.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ClearsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_ClearsArchivedFlagAndTimestamp));
        var idea = CreateIdea(DefaultUserId, isArchived: true);
        idea.ArchivedAtUtc = DateTime.UtcNow;
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.RestoreAsync(idea.Id);

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeFalse();
        stored.ArchivedAtUtc.Should().BeNull();
    }

    // ── Convert to project ────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToProjectAsync_CreatesProjectAndMarksIdeaConverted()
    {
        var (sut, db) = BuildService(nameof(ConvertToProjectAsync_CreatesProjectAndMarksIdeaConverted));
        var idea = CreateIdea(DefaultUserId, "Launch newsletter");
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.ConvertToProjectAsync(idea.Id);

        var project = await db.Projects.AsNoTracking().SingleAsync();
        project.Name.Should().Be("Launch newsletter");
        project.UserId.Should().Be(DefaultUserId);
        project.Status.Should().Be(ProjectStatus.NotStarted);
        (await db.Ideas.AsNoTracking().SingleAsync()).Status.Should().Be(IdeaStatus.ConvertedToProject);
    }

    [Fact]
    public async Task ConvertToProjectAsync_WhenAlreadyConverted_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(ConvertToProjectAsync_WhenAlreadyConverted_ThrowsInvalidOperationException));
        var idea = CreateIdea(DefaultUserId, status: IdeaStatus.ConvertedToProject);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var act = () => sut.ConvertToProjectAsync(idea.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Convert to note ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToNoteAsync_CreatesActiveResourceNoteAndMarksIdeaConverted()
    {
        var (sut, db) = BuildService(nameof(ConvertToNoteAsync_CreatesActiveResourceNoteAndMarksIdeaConverted));
        var idea = CreateIdea(DefaultUserId, "Reading list app");
        idea.Description = "Track books";
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var noteId = await sut.ConvertToNoteAsync(idea.Id);

        var note = await db.Notes.AsNoTracking().SingleAsync(n => n.Id == noteId);
        note.Title.Should().Be("Reading list app");
        note.Content.Should().Be("Track books");
        note.Status.Should().Be(NoteStatus.Active);
        note.ParaCategory.Should().Be(ParaCategory.Resource);
        (await db.Ideas.AsNoTracking().SingleAsync()).Status.Should().Be(IdeaStatus.ConvertedToNote);
    }

    // ── Convert to task ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToTaskAsync_CreatesTodoTaskInProjectAndMarksIdeaConverted()
    {
        var (sut, db) = BuildService(nameof(ConvertToTaskAsync_CreatesTodoTaskInProjectAndMarksIdeaConverted));
        var idea = CreateIdea(DefaultUserId, "Add dark mode");
        var project = CreateProject(DefaultUserId);
        db.Ideas.Add(idea);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var taskId = await sut.ConvertToTaskAsync(idea.Id, project.Id);

        var task = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Title.Should().Be("Add dark mode");
        task.ProjectId.Should().Be(project.Id);
        task.Status.Should().Be(TaskItemStatus.Todo);
        (await db.Ideas.AsNoTracking().SingleAsync()).Status.Should().Be(IdeaStatus.ConvertedToTask);
    }

    [Fact]
    public async Task ConvertToTaskAsync_WhenProjectBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(ConvertToTaskAsync_WhenProjectBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var idea = CreateIdea(DefaultUserId);
        var foreignProject = CreateProject(OtherUserId);
        db.Ideas.Add(idea);
        db.Projects.Add(foreignProject);
        await db.SaveChangesAsync();

        var act = () => sut.ConvertToTaskAsync(idea.Id, foreignProject.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenIdeaBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenIdeaBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateIdea(OtherUserId);
        db.Ideas.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(foreign.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
