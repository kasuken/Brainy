using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IOfflineCaptureSyncService"/>, the server-side counterpart of the
/// Offline Lite (issue #302) client-side capture queue behind
/// <c>POST /api/offline/captures/sync</c>. Resolved via the real DI container with an EF Core
/// InMemory database, mirroring <c>ShareCaptureServiceTests</c>'s style.
/// </summary>
public class OfflineCaptureSyncServiceTests
{
    private const string DefaultUserId = "offline-sync-user-1";
    private const string OtherUserId = "offline-sync-user-2";

    private static (IOfflineCaptureSyncService Service, BrainyDbContext Db) BuildService(
        string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IOfflineCaptureSyncService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task SyncAsync_NewItem_CreatesANoteAndReportsCreated()
    {
        var (sut, db) = BuildService(nameof(SyncAsync_NewItem_CreatesANoteAndReportsCreated));
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), "Offline idea", "captured while offline", null)
        ]);

        var result = await sut.SyncAsync(batch);

        result.Items.Should().ContainSingle();
        result.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        result.Items[0].NoteId.Should().NotBeNull();
        (await db.Notes.CountAsync()).Should().Be(1);
        (await db.OfflineCaptureSyncRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_SameIdempotencyKeyTwice_SecondAttemptIsAlreadySyncedAndCreatesNoSecondNote()
    {
        var dbName = nameof(SyncAsync_SameIdempotencyKeyTwice_SecondAttemptIsAlreadySyncedAndCreatesNoSecondNote);
        var (sut, db) = BuildService(dbName);
        var key = Guid.NewGuid();
        var item = new OfflineCaptureSyncItemDto(key, null, "retry me", null);

        var first = await sut.SyncAsync(new OfflineCaptureSyncBatchDto([item]));
        var second = await sut.SyncAsync(new OfflineCaptureSyncBatchDto([item]));

        first.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        second.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.AlreadySynced);
        second.Items[0].NoteId.Should().Be(first.Items[0].NoteId);
        (await db.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_BatchWithMultipleItems_CreatesASeparateNoteForEach()
    {
        var dbName = nameof(SyncAsync_BatchWithMultipleItems_CreatesASeparateNoteForEach);
        var (sut, db) = BuildService(dbName);
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "first capture", null),
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "second capture", null),
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "third capture", null)
        ]);

        var result = await sut.SyncAsync(batch);

        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(i => i.Outcome == OfflineCaptureSyncOutcome.Created);
        (await db.Notes.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task SyncAsync_ItemWithNothingCapturable_IsRejectedAndCreatesNoNote()
    {
        var (sut, db) = BuildService(nameof(SyncAsync_ItemWithNothingCapturable_IsRejectedAndCreatesNoNote));
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, null, null)
        ]);

        var result = await sut.SyncAsync(batch);

        result.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Rejected);
        result.Items[0].Error.Should().NotBeNullOrWhiteSpace();
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_OneRejectedItemInABatch_DoesNotAffectTheOthers()
    {
        var dbName = nameof(SyncAsync_OneRejectedItemInABatch_DoesNotAffectTheOthers);
        var (sut, db) = BuildService(dbName);
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "good capture", null),
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, null, null)
        ]);

        var result = await sut.SyncAsync(batch);

        result.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        result.Items[1].Outcome.Should().Be(OfflineCaptureSyncOutcome.Rejected);
        (await db.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_OversizedBatch_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(SyncAsync_OversizedBatch_ThrowsArgumentException));
        var items = Enumerable.Range(0, IOfflineCaptureSyncService.MaxBatchSize + 1)
            .Select(i => new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, $"item {i}", null))
            .ToList();

        var act = () => sut.SyncAsync(new OfflineCaptureSyncBatchDto(items));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SyncAsync_EmptyBatch_ReturnsNoItemsAndCreatesNothing()
    {
        var (sut, db) = BuildService(nameof(SyncAsync_EmptyBatch_ReturnsNoItemsAndCreatesNothing));

        var result = await sut.SyncAsync(new OfflineCaptureSyncBatchDto([]));

        result.Items.Should().BeEmpty();
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_SameIdempotencyKeyFromDifferentUsers_CreatesASeparateNoteForEach()
    {
        var dbName = nameof(SyncAsync_SameIdempotencyKeyFromDifferentUsers_CreatesASeparateNoteForEach);
        var (sutA, db) = BuildService(dbName, DefaultUserId);
        var (sutB, _) = BuildService(dbName, OtherUserId);
        var sharedKey = Guid.NewGuid();
        var item = new OfflineCaptureSyncItemDto(sharedKey, null, "coincidentally identical key", null);

        var resultA = await sutA.SyncAsync(new OfflineCaptureSyncBatchDto([item]));
        var resultB = await sutB.SyncAsync(new OfflineCaptureSyncBatchDto([item]));

        // Per-user scoping (AGENTS.md): the idempotency ledger is keyed by (UserId,
        // IdempotencyKey), so an (astronomically unlikely) cross-user key collision must
        // never cause user B's genuine capture to be silently dropped as "already synced".
        resultA.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        resultB.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        resultA.Items[0].NoteId.Should().NotBeNull();
        resultB.Items[0].NoteId.Should().NotBeNull();
        resultA.Items[0].NoteId!.Value.Should().NotBe(resultB.Items[0].NoteId!.Value);
        (await db.Notes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_NoteCreatedForOneUser_IsOwnedByThatUserOnly()
    {
        var dbName = nameof(SyncAsync_NoteCreatedForOneUser_IsOwnedByThatUserOnly);
        var (sut, db) = BuildService(dbName, DefaultUserId);
        await sut.SyncAsync(new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "mine", null)
        ]));

        var note = await db.Notes.SingleAsync();

        note.UserId.Should().Be(DefaultUserId);
    }
}
