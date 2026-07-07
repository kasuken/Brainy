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
/// Unit tests for <see cref="ISearchService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Search terms use exact casing because the InMemory provider compares ordinally,
/// while SQL Server behavior depends on the database collation.
/// </summary>
public class SearchServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (ISearchService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<ISearchService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Note CreateNote(string userId, string title, string content = "", bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, Content = content, IsArchived = isArchived };

    private static Area CreateArea(string userId, string name, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = name, IsArchived = isArchived };

    private static Project CreateProject(string userId, string name, Guid? areaId = null, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = name, AreaId = areaId, IsArchived = isArchived };

    private static TaskItem CreateTask(string userId, Guid projectId, string title, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, ProjectId = projectId, Title = title, IsArchived = isArchived };

    private static Output CreateOutput(string userId, string title, string content = "", bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, Content = content, IsArchived = isArchived };

    private static Goal CreateGoal(string userId, string title, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, IsArchived = isArchived };

    private static Idea CreateIdea(string userId, string title, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, IsArchived = isArchived };

    // ── Empty / whitespace queries ────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_WithEmptyOrWhitespaceQuery_ReturnsEmpty(string? query)
    {
        var (sut, db) = BuildService($"{nameof(SearchAsync_WithEmptyOrWhitespaceQuery_ReturnsEmpty)}_{query?.Length ?? -1}");
        db.Notes.Add(CreateNote(DefaultUserId, "anything"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync(query!);

        result.Should().BeEmpty();
    }

    // ── Matching & result types ───────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_MatchesNoteByTitle_ReturnsNoteResult()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesNoteByTitle_ReturnsNoteResult));
        db.Notes.Add(CreateNote(DefaultUserId, "Quarterly zettelkasten review"));
        db.Notes.Add(CreateNote(DefaultUserId, "Unrelated"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("zettelkasten");

        result.Should().ContainSingle()
            .Which.ResultType.Should().Be("Note");
    }

    [Fact]
    public async Task SearchAsync_MatchesNoteByContentOnly_ReturnsIt()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesNoteByContentOnly_ReturnsIt));
        db.Notes.Add(CreateNote(DefaultUserId, "Weekly log", content: "Discussed the zettelkasten method today."));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("zettelkasten");

        result.Should().ContainSingle()
            .Which.ContentSnippet.Should().Contain("zettelkasten");
    }

    [Fact]
    public async Task SearchAsync_ReturnsAllMatchingEntityTypes()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_ReturnsAllMatchingEntityTypes));
        var area = CreateArea(DefaultUserId, "voyager area");
        var project = CreateProject(DefaultUserId, "voyager project", area.Id);
        db.Areas.Add(area);
        db.Projects.Add(project);
        db.Notes.Add(CreateNote(DefaultUserId, "voyager note"));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, "voyager task"));
        db.Outputs.Add(CreateOutput(DefaultUserId, "voyager output"));
        db.Goals.Add(CreateGoal(DefaultUserId, "voyager goal"));
        db.Ideas.Add(CreateIdea(DefaultUserId, "voyager idea"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Select(r => r.ResultType).Should()
            .BeEquivalentTo("Note", "Output", "Project", "Area", "Task", "Goal", "Idea");
    }

    // ── Ranking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_RanksTitleMatchAboveContentOnlyMatch()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_RanksTitleMatchAboveContentOnlyMatch));
        var contentOnly = CreateNote(DefaultUserId, "Meeting minutes", content: "Mentioned voyager once.");
        var titleMatch = CreateNote(DefaultUserId, "voyager plan");
        db.Notes.AddRange(contentOnly, titleMatch);
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(titleMatch.Id);
        result[1].Id.Should().Be(contentOnly.Id);
    }

    // ── Exclusions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ExcludesArchivedItems()
    {
        // Archived outputs, projects, areas, tasks, goals, and ideas must never
        // surface in search results (product rule: archives stay in Archives).
        var (sut, db) = BuildService(nameof(SearchAsync_ExcludesArchivedItems));
        var area = CreateArea(DefaultUserId, "voyager area", isArchived: true);
        var project = CreateProject(DefaultUserId, "voyager project", isArchived: true);
        db.Areas.Add(area);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, "voyager task", isArchived: true));
        db.Outputs.Add(CreateOutput(DefaultUserId, "voyager output", isArchived: true));
        db.Goals.Add(CreateGoal(DefaultUserId, "voyager goal", isArchived: true));
        db.Ideas.Add(CreateIdea(DefaultUserId, "voyager idea", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ExcludesOtherUsersData()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_ExcludesOtherUsersData));
        var otherProject = CreateProject(OtherUserId, "voyager project");
        db.Projects.Add(otherProject);
        db.Notes.Add(CreateNote(OtherUserId, "voyager note"));
        db.Tasks.Add(CreateTask(OtherUserId, otherProject.Id, "voyager task"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Should().BeEmpty();
    }

    // ── Result cap ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_CapsResultsAtOneHundred()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_CapsResultsAtOneHundred));
        for (var i = 0; i < 110; i++)
            db.Notes.Add(CreateNote(DefaultUserId, $"voyager note {i}"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Should().HaveCount(100);
    }

    // ── Snippets ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_SnippetCentersAroundTermInLongContent()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_SnippetCentersAroundTermInLongContent));
        var content = new string('a', 500) + " voyager " + new string('b', 500);
        db.Notes.Add(CreateNote(DefaultUserId, "Long note", content: content));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var snippet = result.Single().ContentSnippet;
        snippet.Should().Contain("voyager");
        snippet.Should().StartWith("…").And.EndWith("…");
    }

    [Fact]
    public async Task SearchAsync_SnippetFallsBackToContentStart_WhenTermOnlyInTitle()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_SnippetFallsBackToContentStart_WhenTermOnlyInTitle));
        var content = new string('x', 300);
        db.Notes.Add(CreateNote(DefaultUserId, "voyager", content: content));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var snippet = result.Single().ContentSnippet;
        snippet.Should().StartWith("xxx").And.EndWith("…");
        snippet.Length.Should().Be(201); // 200 chars + trailing ellipsis
    }
}
