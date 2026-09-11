using Brainy.Application.DTOs.Capture;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IShareCaptureService"/>, the service behind Brainy's minimal
/// capture route (issue #298), resolved via the real DI container with an EF Core InMemory
/// database. Each test uses a unique database name for isolation.
/// </summary>
public class ShareCaptureServiceTests
{
    private const string DefaultUserId = "capture-user-1";
    private const string OtherUserId = "capture-user-2";

    private static (IShareCaptureService Service, FixedTimeProvider Clock) BuildService(
        string dbName, string userId = DefaultUserId)
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(clock);

        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IShareCaptureService>(), clock);
    }

    private static INoteService BuildNoteService(string dbName, string userId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();
        return services.BuildServiceProvider().GetRequiredService<INoteService>();
    }

    // ── Basic capture ────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_WithUrlAndText_CreatesInboxNoteWithLinkedSharedLinkSource()
    {
        var dbName = nameof(CaptureAsync_WithUrlAndText_CreatesInboxNoteWithLinkedSharedLinkSource);
        var (sut, _) = BuildService(dbName);

        var result = await sut.CaptureAsync(new ShareCaptureDto(
            "Great article", "Worth revisiting for the API design section.", "https://example.com/article"));

        result.Outcome.Should().Be(ShareCaptureOutcome.Created);
        result.Note.Title.Should().Be("Great article");
        result.Note.Content.Should().Be("Worth revisiting for the API design section.");
        result.Note.SourceUrl.Should().Be("https://example.com/article");
        result.Note.Status.Should().Be(NoteStatus.Inbox);
        result.Note.ProcessedAtUtc.Should().BeNull();

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        var source = await context.Sources.SingleAsync();
        source.Type.Should().Be(SourceType.SharedLink);
    }

    [Fact]
    public async Task CaptureAsync_WithOnlyText_DoesNotCreateASource()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithOnlyText_DoesNotCreateASource));

        var result = await sut.CaptureAsync(new ShareCaptureDto(null, "Just a quick thought", null));

        result.Note.SourceUrl.Should().BeNull();
        result.Note.Title.Should().Be("Just a quick thought");
    }

    [Fact]
    public async Task CaptureAsync_WithUrlOnly_DerivesTitleFromTheUrl()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithUrlOnly_DerivesTitleFromTheUrl));

        var result = await sut.CaptureAsync(new ShareCaptureDto(null, null, "https://example.com/some/article"));

        result.Note.Title.Should().Be("example.com/some/article");
    }

    [Fact]
    public async Task CaptureAsync_KeepsUserTextAndSourceMetadataDistinguishable()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_KeepsUserTextAndSourceMetadataDistinguishable));

        var result = await sut.CaptureAsync(new ShareCaptureDto(
            "Page title from the browser", "My own note about why this matters", "https://example.com/x"));

        result.Note.SourceTitle.Should().Be("Page title from the browser");
        result.Note.Content.Should().Be("My own note about why this matters");
        result.Note.Content.Should().NotBe(result.Note.SourceTitle);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_WithNothingSupplied_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithNothingSupplied_ThrowsArgumentException));

        var act = () => sut.CaptureAsync(new ShareCaptureDto(null, null, null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureAsync_WithOversizedText_ThrowsArgumentExceptionAndPersistsNothing()
    {
        var dbName = nameof(CaptureAsync_WithOversizedText_ThrowsArgumentExceptionAndPersistsNothing);
        var (sut, _) = BuildService(dbName);
        var oversizedText = new string('a', IShareCaptureService.MaxTextLength + 1);

        var act = () => sut.CaptureAsync(new ShareCaptureDto(null, oversizedText, null));

        await act.Should().ThrowAsync<ArgumentException>();

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CaptureAsync_WithOversizedTitle_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithOversizedTitle_ThrowsArgumentException));
        var oversizedTitle = new string('t', IShareCaptureService.MaxTitleLength + 1);

        var act = () => sut.CaptureAsync(new ShareCaptureDto(oversizedTitle, "text", null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureAsync_WithOversizedUrl_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithOversizedUrl_ThrowsArgumentException));
        var oversizedUrl = "https://example.com/" + new string('a', IShareCaptureService.MaxUrlLength);

        var act = () => sut.CaptureAsync(new ShareCaptureDto(null, "text", oversizedUrl));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]
    public async Task CaptureAsync_WithNonHttpUrl_ThrowsArgumentException(string invalidUrl)
    {
        var (sut, _) = BuildService($"{nameof(CaptureAsync_WithNonHttpUrl_ThrowsArgumentException)}-{invalidUrl.GetHashCode()}");

        var act = () => sut.CaptureAsync(new ShareCaptureDto("Title", "text", invalidUrl));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(CaptureAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.CaptureAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Duplicate-retry idempotency ──────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_CalledTwiceWithIdenticalShare_IsIdempotent()
    {
        var dbName = nameof(CaptureAsync_CalledTwiceWithIdenticalShare_IsIdempotent);
        var (sut, _) = BuildService(dbName);
        var dto = new ShareCaptureDto("Retry me", "same content", "https://example.com/x");

        var first = await sut.CaptureAsync(dto);
        var second = await sut.CaptureAsync(dto);

        first.Outcome.Should().Be(ShareCaptureOutcome.Created);
        second.Outcome.Should().Be(ShareCaptureOutcome.DuplicateIgnored);
        second.Note.Id.Should().Be(first.Note.Id);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_RetriedThreeTimesInARow_StillCreatesOnlyOneNote()
    {
        var dbName = nameof(CaptureAsync_RetriedThreeTimesInARow_StillCreatesOnlyOneNote);
        var (sut, _) = BuildService(dbName);
        var dto = new ShareCaptureDto(null, "flaky share sheet retry", null);

        await sut.CaptureAsync(dto);
        await sut.CaptureAsync(dto);
        await sut.CaptureAsync(dto);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_AfterDuplicateWindowElapses_CreatesANewNote()
    {
        var dbName = nameof(CaptureAsync_AfterDuplicateWindowElapses_CreatesANewNote);
        var (sut, clock) = BuildService(dbName);
        var dto = new ShareCaptureDto("Retry me", "same content", "https://example.com/x");

        var first = await sut.CaptureAsync(dto);
        clock.Advance(IShareCaptureService.DuplicateWindow + TimeSpan.FromSeconds(1));
        var second = await sut.CaptureAsync(dto);

        second.Outcome.Should().Be(ShareCaptureOutcome.Created);
        second.Note.Id.Should().NotBe(first.Note.Id);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CaptureAsync_WithDifferentTextFromAPriorShare_IsNotTreatedAsADuplicate()
    {
        var dbName = nameof(CaptureAsync_WithDifferentTextFromAPriorShare_IsNotTreatedAsADuplicate);
        var (sut, _) = BuildService(dbName);

        var first = await sut.CaptureAsync(new ShareCaptureDto(null, "first thought", null));
        var second = await sut.CaptureAsync(new ShareCaptureDto(null, "second, unrelated thought", null));

        second.Outcome.Should().Be(ShareCaptureOutcome.Created);
        second.Note.Id.Should().NotBe(first.Note.Id);
    }

    // ── Per-user isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_SameShareByDifferentUsers_CreatesSeparateNotesForEach()
    {
        var dbName = nameof(CaptureAsync_SameShareByDifferentUsers_CreatesSeparateNotesForEach);
        var (userA, _) = BuildService(dbName, DefaultUserId);
        var (userB, _) = BuildService(dbName, OtherUserId);
        var dto = new ShareCaptureDto("Shared title", "shared text", "https://example.com/shared");

        var resultA = await userA.CaptureAsync(dto);
        var resultB = await userB.CaptureAsync(dto);

        resultA.Outcome.Should().Be(ShareCaptureOutcome.Created);
        resultB.Outcome.Should().Be(ShareCaptureOutcome.Created);
        resultA.Note.Id.Should().NotBe(resultB.Note.Id);
    }

    [Fact]
    public async Task CaptureAsync_NoteCreatedByOneUser_IsNotVisibleToAnotherUser()
    {
        var dbName = nameof(CaptureAsync_NoteCreatedByOneUser_IsNotVisibleToAnotherUser);
        var (userA, _) = BuildService(dbName, DefaultUserId);

        var createdByA = await userA.CaptureAsync(new ShareCaptureDto("A's title", "A's private text", null));

        var noteServiceB = BuildNoteService(dbName, OtherUserId);
        var seenByB = await noteServiceB.GetByIdAsync(createdByA.Note.Id);

        seenByB.Should().BeNull();
    }
}
