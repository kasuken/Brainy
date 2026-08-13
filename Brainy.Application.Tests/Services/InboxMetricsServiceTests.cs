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
/// Unit tests for <see cref="IInboxMetricsService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Created timestamps are audit-stamped at save time, so age/average assertions work
/// with just-created data (age 0, small day deltas) rather than seeded historic dates.
/// </summary>
public class InboxMetricsServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IInboxMetricsService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IInboxMetricsService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Note CreateNote(
        string userId,
        NoteStatus status = NoteStatus.Inbox,
        bool isArchived = false,
        DateTime? processedAtUtc = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Note",
            Status = status,
            IsArchived = isArchived,
            ProcessedAtUtc = processedAtUtc
        };

    [Fact]
    public async Task GetMetricsAsync_WithNoNotes_ReturnsEmptyMetrics()
    {
        var (sut, _) = BuildService(nameof(GetMetricsAsync_WithNoNotes_ReturnsEmptyMetrics));

        var result = await sut.GetMetricsAsync();

        result.WaitingCount.Should().Be(0);
        result.OldestItemAgeInDays.Should().BeNull();
        result.AvgProcessingDays.Should().BeNull();
    }

    [Fact]
    public async Task GetMetricsAsync_CountsOnlyActiveInboxNotes()
    {
        var (sut, db) = BuildService(nameof(GetMetricsAsync_CountsOnlyActiveInboxNotes));
        db.Notes.Add(CreateNote(DefaultUserId));
        db.Notes.Add(CreateNote(DefaultUserId));
        db.Notes.Add(CreateNote(DefaultUserId, status: NoteStatus.Active));
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetMetricsAsync();

        result.WaitingCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMetricsAsync_ExcludesOtherUsersNotes()
    {
        var (sut, db) = BuildService(nameof(GetMetricsAsync_ExcludesOtherUsersNotes));
        db.Notes.Add(CreateNote(OtherUserId));
        await db.SaveChangesAsync();

        var result = await sut.GetMetricsAsync();

        result.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMetricsAsync_ReportsAgeZeroForJustCapturedNotes()
    {
        var (sut, db) = BuildService(nameof(GetMetricsAsync_ReportsAgeZeroForJustCapturedNotes));
        db.Notes.Add(CreateNote(DefaultUserId));
        await db.SaveChangesAsync();

        var result = await sut.GetMetricsAsync();

        result.OldestItemAgeInDays.Should().Be(0);
    }

    [Fact]
    public async Task GetMetricsAsync_AveragesProcessingTimeOfProcessedNotes()
    {
        var (sut, db) = BuildService(nameof(GetMetricsAsync_AveragesProcessingTimeOfProcessedNotes));
        // CreatedAtUtc is stamped "now" at save; processed two days later => ~2 days.
        db.Notes.Add(CreateNote(DefaultUserId, status: NoteStatus.Active,
            processedAtUtc: DateTime.UtcNow.AddDays(2)));
        await db.SaveChangesAsync();

        var result = await sut.GetMetricsAsync();

        result.AvgProcessingDays.Should().NotBeNull();
        result.AvgProcessingDays!.Value.Should().BeApproximately(2, 0.1);
    }
}
