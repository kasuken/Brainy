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

/// <summary>
/// Unit tests for <see cref="IRelatedNotesService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Similarity is word-level Jaccard: words shorter than 3 characters and stop words
/// are ignored, and title words weigh double.
/// </summary>
public class RelatedNotesServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IRelatedNotesService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IRelatedNotesService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Note CreateNote(string userId, string title, string content = "")
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title, Content = content };

    // ── GetRelatedAsync (pivot loaded from the database) ──────────────────────

    [Fact]
    public async Task GetRelatedAsync_WhenPivotNoteDoesNotExist_ReturnsEmpty()
    {
        var (sut, _) = BuildService(nameof(GetRelatedAsync_WhenPivotNoteDoesNotExist_ReturnsEmpty));

        var result = await sut.GetRelatedAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedAsync_WhenPivotBelongsToAnotherUser_ReturnsEmpty()
    {
        var (sut, db) = BuildService(nameof(GetRelatedAsync_WhenPivotBelongsToAnotherUser_ReturnsEmpty));
        var foreign = CreateNote(OtherUserId, "quantum computing basics");
        db.Notes.Add(foreign);
        db.Notes.Add(CreateNote(OtherUserId, "quantum computing advanced"));
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedAsync(foreign.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedAsync_ReturnsNotesSharingWords()
    {
        var (sut, db) = BuildService(nameof(GetRelatedAsync_ReturnsNotesSharingWords));
        var pivot = CreateNote(DefaultUserId, "quantum computing basics");
        var related = CreateNote(DefaultUserId, "quantum computing advanced");
        var unrelated = CreateNote(DefaultUserId, "gardening tips");
        db.Notes.AddRange(pivot, related, unrelated);
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedAsync(pivot.Id);

        result.Should().ContainSingle()
            .Which.Id.Should().Be(related.Id);
    }

    [Fact]
    public async Task GetRelatedAsync_ExcludesThePivotNoteItself()
    {
        var (sut, db) = BuildService(nameof(GetRelatedAsync_ExcludesThePivotNoteItself));
        var pivot = CreateNote(DefaultUserId, "quantum computing basics");
        db.Notes.Add(pivot);
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedAsync(pivot.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedAsync_ExcludesOtherUsersNotes()
    {
        var (sut, db) = BuildService(nameof(GetRelatedAsync_ExcludesOtherUsersNotes));
        var pivot = CreateNote(DefaultUserId, "quantum computing basics");
        db.Notes.Add(pivot);
        db.Notes.Add(CreateNote(OtherUserId, "quantum computing advanced"));
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedAsync(pivot.Id);

        result.Should().BeEmpty();
    }

    // ── GetRelatedByContentAsync (pivot supplied by the caller) ───────────────

    [Fact]
    public async Task GetRelatedByContentAsync_OrdersByDescendingSimilarity()
    {
        var (sut, db) = BuildService(nameof(GetRelatedByContentAsync_OrdersByDescendingSimilarity));
        var strong = CreateNote(DefaultUserId, "quantum computing hardware roadmap");
        var weak = CreateNote(DefaultUserId, "quantum gardening");
        db.Notes.AddRange(strong, weak);
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedByContentAsync(
            Guid.NewGuid(), "quantum computing hardware roadmap", string.Empty);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(strong.Id);
        result[1].Id.Should().Be(weak.Id);
        result[0].SimilarityScore.Should().BeGreaterThan(result[1].SimilarityScore);
    }

    [Fact]
    public async Task GetRelatedByContentAsync_CapsResultsAtTopN()
    {
        var (sut, db) = BuildService(nameof(GetRelatedByContentAsync_CapsResultsAtTopN));
        for (var i = 0; i < 5; i++)
            db.Notes.Add(CreateNote(DefaultUserId, $"quantum computing note {i}"));
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedByContentAsync(
            Guid.NewGuid(), "quantum computing", string.Empty, topN: 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRelatedByContentAsync_WithStopWordsAndShortWordsOnly_ReturnsEmpty()
    {
        var (sut, db) = BuildService(nameof(GetRelatedByContentAsync_WithStopWordsAndShortWordsOnly_ReturnsEmpty));
        db.Notes.Add(CreateNote(DefaultUserId, "the and for a to"));
        await db.SaveChangesAsync();

        // Pivot text tokenizes to nothing: stop words and words under 3 characters are dropped.
        var result = await sut.GetRelatedByContentAsync(
            Guid.NewGuid(), "the and for", "a to it");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedByContentAsync_MatchesWordsCaseInsensitively()
    {
        var (sut, db) = BuildService(nameof(GetRelatedByContentAsync_MatchesWordsCaseInsensitively));
        var related = CreateNote(DefaultUserId, "QUANTUM COMPUTING");
        db.Notes.Add(related);
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedByContentAsync(
            Guid.NewGuid(), "quantum computing", string.Empty);

        result.Should().ContainSingle()
            .Which.Id.Should().Be(related.Id);
    }

    [Fact]
    public async Task GetRelatedByContentAsync_MatchesWordsInsideMarkdownSyntax()
    {
        var (sut, db) = BuildService(nameof(GetRelatedByContentAsync_MatchesWordsInsideMarkdownSyntax));
        var related = CreateNote(DefaultUserId, "Plain note", content: "# quantum\n**computing** [roadmap](https://example.test)");
        db.Notes.Add(related);
        await db.SaveChangesAsync();

        var result = await sut.GetRelatedByContentAsync(
            Guid.NewGuid(), "quantum computing roadmap", string.Empty);

        result.Should().ContainSingle()
            .Which.Id.Should().Be(related.Id);
    }
}
