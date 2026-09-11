using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Integration-style tests (EF InMemory + full DI container) covering the guided weekly
/// review's own additions on top of Week: Inbox age banding, resurfacing candidate
/// selection and rationale, dismissal persistence, and the small decision set. Per-user
/// scope and archive-exclusion are covered explicitly (issue #299 acceptance criteria).
///
/// <c>BrainyDbContext</c> stamps <c>CreatedAtUtc</c>/<c>UpdatedAtUtc</c> from the shared
/// <see cref="TimeProvider"/> at save time — they cannot be set directly on the entity and
/// survive a save. Tests that need "old" notes save them, then advance a
/// <see cref="FixedTimeProvider"/> forward before querying, exactly like
/// <c>TasksHubServiceTests.GetStaleTasksAsync_*</c>.
/// </summary>
public sealed class WeeklyReviewServiceTests
{
    private const string DefaultUserId = "review-user";
    private static readonly DateTimeOffset DefaultStart = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    [Fact]
    public async Task GetCurrentReviewAsync_BucketsUnprocessedNotesByAge()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(GetCurrentReviewAsync_BucketsUnprocessedNotesByAge), clock: clock);

        var monthPlus = CreateNote(DefaultUserId, "From two months ago");
        fixture.Db.Add(monthPlus);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(46));

        var fewWeeks = CreateNote(DefaultUserId, "From two weeks ago");
        fixture.Db.Add(fewWeeks);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(11));

        var thisWeek = CreateNote(DefaultUserId, "From three days ago");
        fixture.Db.Add(thisWeek);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(3));

        var today = CreateNote(DefaultUserId, "Captured today");
        fixture.Db.Add(today);
        await fixture.Db.SaveChangesAsync();

        var review = await fixture.Review.GetCurrentReviewAsync();

        review.InboxTotalCount.Should().Be(4);
        review.InboxAgeBands.Should().ContainSingle(band => band.Key == "today" && band.Items.Any(item => item.NoteId == today.Id));
        review.InboxAgeBands.Should().ContainSingle(band => band.Key == "this-week" && band.Items.Any(item => item.NoteId == thisWeek.Id));
        review.InboxAgeBands.Should().ContainSingle(band => band.Key == "1-4-weeks" && band.Items.Any(item => item.NoteId == fewWeeks.Id));
        review.InboxAgeBands.Should().ContainSingle(band => band.Key == "1-month-plus" && band.Items.Any(item => item.NoteId == monthPlus.Id));
    }

    [Fact]
    public async Task GetCurrentReviewAsync_ExcludesProcessedAndArchivedNotesFromTheInbox()
    {
        var fixture = BuildFixture(nameof(GetCurrentReviewAsync_ExcludesProcessedAndArchivedNotesFromTheInbox));
        var processed = CreateNote(DefaultUserId, "Already processed", processedAtUtc: DateTime.UtcNow);
        var archived = CreateNote(DefaultUserId, "Archived inbox note", isArchived: true);
        fixture.Db.AddRange(processed, archived);
        await fixture.Db.SaveChangesAsync();

        var review = await fixture.Review.GetCurrentReviewAsync();

        review.InboxTotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentReviewAsync_ResurfacesStaleUsefulNotes_WithProjectRationale()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(GetCurrentReviewAsync_ResurfacesStaleUsefulNotes_WithProjectRationale), clock: clock);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Name = "Rebuild the deck",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.Medium
        };
        var staleLinkedNote = CreateNote(DefaultUserId, "Deck research", processedAtUtc: DateTime.UtcNow);
        staleLinkedNote.ProjectId = project.Id;
        fixture.Db.AddRange(project, staleLinkedNote);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(30));

        var review = await fixture.Review.GetCurrentReviewAsync();

        var resurfaced = review.ResurfacedNotes.Should().ContainSingle(note => note.NoteId == staleLinkedNote.Id).Subject;
        resurfaced.Rationale.Should().Contain("Rebuild the deck");
        resurfaced.ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task GetCurrentReviewAsync_ExcludesRecentlyTouchedNotesAndNotesWithNoUsefulSignal()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(GetCurrentReviewAsync_ExcludesRecentlyTouchedNotesAndNotesWithNoUsefulSignal), clock: clock);

        var recentlyTouchedFavorite = CreateNote(DefaultUserId, "Recently edited favorite", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        var plainStaleNote = CreateNote(DefaultUserId, "Plain stale note, no signal", processedAtUtc: DateTime.UtcNow);
        fixture.Db.AddRange(recentlyTouchedFavorite, plainStaleNote);
        await fixture.Db.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(20));
        // Touch only the favorite note, simulating a recent edit that resets its recency signal.
        fixture.Db.Entry(recentlyTouchedFavorite).State = EntityState.Modified;
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(1));

        var review = await fixture.Review.GetCurrentReviewAsync();

        review.ResurfacedNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentReviewAsync_ScopesInboxAndResurfacingToTheAuthenticatedUser()
    {
        var dbName = nameof(GetCurrentReviewAsync_ScopesInboxAndResurfacingToTheAuthenticatedUser);
        var clock = new FixedTimeProvider(DefaultStart);
        var ownerFixture = BuildFixture(dbName, userId: "owner", clock: clock);
        var otherFixture = BuildFixture(dbName, userId: "other", clock: clock);

        var ownerFavorite = CreateNote("owner", "Owner's favorite", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        var otherFavorite = CreateNote("other", "Other's favorite", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        ownerFixture.Db.AddRange(ownerFavorite, otherFavorite);
        await ownerFixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(30));

        var ownerInbox = CreateNote("owner", "Owner's inbox note");
        var otherInbox = CreateNote("other", "Other's inbox note");
        ownerFixture.Db.AddRange(ownerInbox, otherInbox);
        await ownerFixture.Db.SaveChangesAsync();

        var ownerReview = await ownerFixture.Review.GetCurrentReviewAsync();
        var otherReview = await otherFixture.Review.GetCurrentReviewAsync();

        ownerReview.InboxAgeBands.SelectMany(band => band.Items).Select(item => item.NoteId).Should().ContainSingle(id => id == ownerInbox.Id);
        ownerReview.InboxAgeBands.SelectMany(band => band.Items).Select(item => item.NoteId).Should().NotContain(otherInbox.Id);
        ownerReview.ResurfacedNotes.Select(note => note.NoteId).Should().ContainSingle(id => id == ownerFavorite.Id);
        ownerReview.ResurfacedNotes.Select(note => note.NoteId).Should().NotContain(otherFavorite.Id);

        otherReview.InboxAgeBands.SelectMany(band => band.Items).Select(item => item.NoteId).Should().ContainSingle(id => id == otherInbox.Id);
        otherReview.ResurfacedNotes.Select(note => note.NoteId).Should().ContainSingle(id => id == otherFavorite.Id);
    }

    [Fact]
    public async Task DismissResurfacedNoteAsync_PersistsAndExcludesTheNoteFromFutureReviews()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(DismissResurfacedNoteAsync_PersistsAndExcludesTheNoteFromFutureReviews), clock: clock);
        var note = CreateNote(DefaultUserId, "Dismiss me", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        fixture.Db.Add(note);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(30));

        (await fixture.Review.GetCurrentReviewAsync()).ResurfacedNotes.Should().ContainSingle(candidate => candidate.NoteId == note.Id);

        await fixture.Review.DismissResurfacedNoteAsync(note.Id);
        await fixture.Review.DismissResurfacedNoteAsync(note.Id); // idempotent

        (await fixture.Db.ResurfacingDismissals.CountAsync()).Should().Be(1);
        (await fixture.Review.GetCurrentReviewAsync()).ResurfacedNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task DismissResurfacedNoteAsync_ThrowsForANoteOwnedByAnotherUser()
    {
        var dbName = nameof(DismissResurfacedNoteAsync_ThrowsForANoteOwnedByAnotherUser);
        var ownerFixture = BuildFixture(dbName, userId: "owner");
        var intruderFixture = BuildFixture(dbName, userId: "intruder");

        var note = CreateNote("owner", "Owner-only note");
        ownerFixture.Db.Add(note);
        await ownerFixture.Db.SaveChangesAsync();

        var act = () => intruderFixture.Review.DismissResurfacedNoteAsync(note.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await ownerFixture.Db.ResurfacingDismissals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task KeepResurfacedNoteAsync_DoesNotSuppressFutureResurfacing()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(KeepResurfacedNoteAsync_DoesNotSuppressFutureResurfacing), clock: clock);
        var note = CreateNote(DefaultUserId, "Keep me around", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        fixture.Db.Add(note);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(30));

        await fixture.Review.KeepResurfacedNoteAsync(note.Id);

        (await fixture.Db.ResurfacingDismissals.CountAsync()).Should().Be(0);
        (await fixture.Review.GetCurrentReviewAsync()).ResurfacedNotes.Should().ContainSingle(candidate => candidate.NoteId == note.Id);
    }

    [Fact]
    public async Task ArchiveResurfacedNoteAsync_ArchivesTheNoteAndExcludesItFromFutureReviews()
    {
        var clock = new FixedTimeProvider(DefaultStart);
        var fixture = BuildFixture(nameof(ArchiveResurfacedNoteAsync_ArchivesTheNoteAndExcludesItFromFutureReviews), clock: clock);
        var note = CreateNote(DefaultUserId, "Archive me", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        fixture.Db.Add(note);
        await fixture.Db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(30));

        await fixture.Review.ArchiveResurfacedNoteAsync(note.Id);

        var reloaded = await fixture.Db.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        reloaded.IsArchived.Should().BeTrue();
        reloaded.ArchivedReason.Should().NotBeNullOrWhiteSpace();
        (await fixture.Review.GetCurrentReviewAsync()).ResurfacedNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task LinkResurfacedNoteAsync_LinksTheNoteToTheGivenProject()
    {
        var fixture = BuildFixture(nameof(LinkResurfacedNoteAsync_LinksTheNoteToTheGivenProject));
        var project = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Target", Status = ProjectStatus.Active };
        var note = CreateNote(DefaultUserId, "Link me", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        fixture.Db.AddRange(project, note);
        await fixture.Db.SaveChangesAsync();

        await fixture.Review.LinkResurfacedNoteAsync(note.Id, project.Id);

        var reloaded = await fixture.Db.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        reloaded.ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task TurnResurfacedNoteIntoTaskAsync_CreatesATaskInTheGivenProject()
    {
        var fixture = BuildFixture(nameof(TurnResurfacedNoteIntoTaskAsync_CreatesATaskInTheGivenProject));
        var project = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Target", Status = ProjectStatus.Active };
        var note = CreateNote(DefaultUserId, "Turn me into a task", processedAtUtc: DateTime.UtcNow, isFavorite: true);
        fixture.Db.AddRange(project, note);
        await fixture.Db.SaveChangesAsync();

        await fixture.Review.TurnResurfacedNoteIntoTaskAsync(note.Id, project.Id);

        var task = await fixture.Db.Tasks.AsNoTracking().SingleAsync(t => t.ProjectId == project.Id);
        task.Title.Should().Be("Turn me into a task");
        (await fixture.Db.ActionItems.CountAsync(a => a.NoteId == note.Id)).Should().Be(1);
    }

    private static Note CreateNote(
        string userId,
        string title,
        DateTime? processedAtUtc = null,
        bool isArchived = false,
        bool isFavorite = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Content = "Content for " + title,
            Status = processedAtUtc is null ? NoteStatus.Inbox : NoteStatus.Active,
            ParaCategory = ParaCategory.Resource,
            ProcessedAtUtc = processedAtUtc,
            IsArchived = isArchived,
            IsFavorite = isFavorite
        };

    private static TestFixture BuildFixture(string dbName, string userId = DefaultUserId, FixedTimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(dbName, DatabaseRoot));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(clock ?? new FixedTimeProvider(DefaultStart));
        services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService(DefaultStart.UtcDateTime));
        services.AddBrainyApplication();
        // ActionItemService (used by TurnResurfacedNoteIntoTaskAsync) depends on IAiAssistant
        // for its AI-extraction path, which this fixture never exercises.
        services.AddDisabledAiAssistant();

        var provider = services.BuildServiceProvider();
        return new TestFixture(
            provider.GetRequiredService<IWeeklyReviewService>(),
            provider.GetRequiredService<BrainyDbContext>(),
            provider);
    }

    private sealed record TestFixture(IWeeklyReviewService Review, BrainyDbContext Db, ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FakeUserTimeZoneService(DateTime today) : IUserTimeZoneService
    {
        public Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
        public Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeZoneInfo.Utc);
        public Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today.Date);
        public Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetTimeZoneOverrideIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetTimeZoneOverrideAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(DateTime localStartDate, DateTime localEndDate, CancellationToken cancellationToken = default)
            => Task.FromResult((localStartDate.Date, localEndDate.Date.AddDays(1)));
    }
}
