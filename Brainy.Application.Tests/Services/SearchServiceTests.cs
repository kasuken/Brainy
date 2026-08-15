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

    private static Resource CreateResource(
        string userId,
        string name,
        string? topic = null,
        string? description = null,
        bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Topic = topic,
            Description = description,
            IsArchived = isArchived
        };

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

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
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

        result.Items.Should().ContainSingle()
            .Which.ResultType.Should().Be("Note");
    }

    [Fact]
    public async Task SearchAsync_MatchesNoteByContentOnly_ReturnsIt()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesNoteByContentOnly_ReturnsIt));
        db.Notes.Add(CreateNote(DefaultUserId, "Weekly log", content: "Discussed the zettelkasten method today."));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("zettelkasten");

        result.Items.Should().ContainSingle()
            .Which.ContentSnippet.Should().Contain("zettelkasten");
    }

    [Fact]
    public async Task SearchAsync_MatchesNoteByTag_AndReturnsTags()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesNoteByTag_AndReturnsTags));
        var tag = new Tag { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "voyager" };
        var note = CreateNote(DefaultUserId, "Mission notes");
        note.Tags.Add(tag);
        db.AddRange(tag, note);
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var match = result.Items.Should().ContainSingle().Which;
        match.ResultType.Should().Be("Note");
        match.Tags.Should().Equal("voyager");
        match.SnippetSource.Should().Be("Tags");
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
        db.Resources.Add(CreateResource(DefaultUserId, "voyager resource"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Items.Select(r => r.ResultType).Should()
            .BeEquivalentTo("Note", "Output", "Project", "Area", "Task", "Goal", "Idea", "Resource");
        result.TotalCount.Should().Be(8);
    }

    [Fact]
    public async Task SearchAsync_MatchesResourceTopicAndReturnsResourceIdentity()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesResourceTopicAndReturnsResourceIdentity));
        var resource = CreateResource(DefaultUserId, "Engineering handbook", topic: "voyager operations");
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var match = result.Items.Should().ContainSingle().Which;
        match.ResultType.Should().Be("Resource");
        match.ParaCategory.Should().Be(ParaCategory.Resource);
        match.ResourceId.Should().Be(resource.Id);
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

        result.Items.Should().HaveCount(2);
        result.Items[0].Id.Should().Be(titleMatch.Id);
        result.Items[1].Id.Should().Be(contentOnly.Id);
    }

    [Fact]
    public async Task SearchAsync_PaginatesAcrossResults_AndReturnsTotalCount()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_PaginatesAcrossResults_AndReturnsTotalCount));
        var notes = Enumerable.Range(1, 25)
            .Select(index => new Note
            {
                Id = Guid.NewGuid(),
                UserId = DefaultUserId,
                Title = $"voyager note {index:D2}",
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(index)
            })
            .ToList();

        db.Notes.AddRange(notes);
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager", page: 2, pageSize: 10);

        result.TotalCount.Should().Be(25);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCount(10);
        result.Items.Select(item => item.Id).Should().Equal(notes
            .OrderByDescending(note => note.UpdatedAtUtc)
            .Skip(10)
            .Take(10)
            .Select(note => note.Id));
    }

    // ── Exclusions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ExcludesArchivedItems()
    {
        // Archived entities must never
        // surface in search results (product rule: archives stay in Archives).
        var (sut, db) = BuildService(nameof(SearchAsync_ExcludesArchivedItems));
        var area = CreateArea(DefaultUserId, "voyager area", isArchived: true);
        var project = CreateProject(DefaultUserId, "voyager project", isArchived: true);
        db.Areas.Add(area);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, "voyager task", isArchived: true));
        db.Notes.Add(CreateNote(DefaultUserId, "voyager note", isArchived: true));
        db.Notes.Add(new Note
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Title = "voyager note by status",
            Status = NoteStatus.Archived
        });
        db.Outputs.Add(CreateOutput(DefaultUserId, "voyager output", isArchived: true));
        db.Goals.Add(CreateGoal(DefaultUserId, "voyager goal", isArchived: true));
        db.Ideas.Add(CreateIdea(DefaultUserId, "voyager idea", isArchived: true));
        db.Resources.Add(CreateResource(DefaultUserId, "voyager resource", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ExcludesOtherUsersData()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_ExcludesOtherUsersData));
        var otherProject = CreateProject(OtherUserId, "voyager project");
        db.Projects.Add(otherProject);
        db.Notes.Add(CreateNote(OtherUserId, "voyager note"));
        db.Tasks.Add(CreateTask(OtherUserId, otherProject.Id, "voyager task"));
        db.Resources.Add(CreateResource(OtherUserId, "voyager resource"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        result.Items.Should().BeEmpty();
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

        var snippet = result.Items.Single().ContentSnippet;
        snippet.Should().Contain("voyager");
        snippet.Should().StartWith("…").And.EndWith("…");
    }

    [Fact]
    public async Task SearchAsync_SnippetUsesMatchedTitle_WhenTermOnlyInTitle()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_SnippetUsesMatchedTitle_WhenTermOnlyInTitle));
        db.Notes.Add(CreateNote(DefaultUserId, "voyager plan", content: "background context"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var match = result.Items.Single();
        match.SnippetSource.Should().Be("Title");
        match.ContentSnippet.Should().Be("voyager plan");
    }

    [Fact]
    public async Task SearchAsync_SnippetReturnsShortContentWithoutTruncation()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_SnippetReturnsShortContentWithoutTruncation));
        db.Notes.Add(CreateNote(DefaultUserId, "Meeting note", content: "Quick voyager summary"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var match = result.Items.Single();
        match.SnippetSource.Should().Be("Content");
        match.ContentSnippet.Should().Be("Quick voyager summary");
    }

    [Fact]
    public async Task SearchAsync_SnippetStripsHtmlLikeContent_BeforeReturning()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_SnippetStripsHtmlLikeContent_BeforeReturning));
        db.Notes.Add(CreateNote(DefaultUserId, "Unsafe note", content: "Alpha <script>alert('x')</script> voyager <b>bold</b>"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("voyager");

        var snippet = result.Items.Single().ContentSnippet;
        snippet.Should().Contain("voyager");
        snippet.Should().NotContain("<script>");
        snippet.Should().NotContain("<b>");
    }
}
