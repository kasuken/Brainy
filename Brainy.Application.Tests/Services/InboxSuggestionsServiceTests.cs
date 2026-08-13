using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IInboxSuggestionsService"/> — a pure keyword heuristic
/// with no database dependency, resolved via the real DI container.
/// </summary>
public class InboxSuggestionsServiceTests
{
    private static IInboxSuggestionsService BuildService()
    {
        var services = new ServiceCollection();
        services.AddBrainyApplication();
        return services.BuildServiceProvider().GetRequiredService<IInboxSuggestionsService>();
    }

    private static NoteDto CreateNote(string title, string content = "") => new(
        Guid.NewGuid(), title, content, AiSummary: null,
        NoteStatus.Inbox, IsArchived: false, ArchivedAtUtc: null, ProcessedAtUtc: null,
        ParaCategory.Project, SourceId: null, ProjectId: null, AreaId: null, ResourceId: null,
        CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    [Theory]
    [InlineData("sprint milestone deliver", ParaCategory.Project)]
    [InlineData("fitness routine habit", ParaCategory.Area)]
    [InlineData("tutorial documentation article", ParaCategory.Resource)]
    [InlineData("outdated deprecated historical", ParaCategory.Archive)]
    public async Task SuggestAsync_SuggestsHighestScoringCategory(string text, ParaCategory expected)
    {
        var sut = BuildService();

        var result = await sut.SuggestAsync(CreateNote(text));

        result.Should().NotBeNull();
        result!.SuggestedCategory.Should().Be(expected);
    }

    [Fact]
    public async Task SuggestAsync_WithNoKeywordMatch_ReturnsNull()
    {
        var sut = BuildService();

        var result = await sut.SuggestAsync(CreateNote("zebra unicorn"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task SuggestAsync_MatchesKeywordsInContentNotJustTitle()
    {
        var sut = BuildService();

        var result = await sut.SuggestAsync(CreateNote("Untitled", content: "sprint milestone deliver"));

        result.Should().NotBeNull();
        result!.SuggestedCategory.Should().Be(ParaCategory.Project);
    }

    [Fact]
    public async Task SuggestAsync_ExplainsWhichKeywordsMatched()
    {
        // Product rule: the AI/heuristic must explain why it made a suggestion.
        var sut = BuildService();

        var result = await sut.SuggestAsync(CreateNote("tutorial documentation article"));

        result.Should().NotBeNull();
        result!.Reasoning.Should().Contain("tutorial");
    }
}
