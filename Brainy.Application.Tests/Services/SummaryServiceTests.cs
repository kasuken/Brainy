using Brainy.Application.DTOs.Summaries;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ISummaryService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Summaries are owned through their parent note; AI provenance must round-trip.
/// </summary>
public class SummaryServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (ISummaryService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<ISummaryService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Note CreateNote(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = "Note" };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithOwnedNote_PersistsTrimmedSummary()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithOwnedNote_PersistsTrimmedSummary));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateSummaryDto(note.Id, "  the essence  "));

        var stored = await db.Summaries.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.Content.Should().Be("the essence");
        stored.IsAiGenerated.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_PreservesAiProvenance()
    {
        // Product rule: AI-generated content must be marked as such, with provenance.
        var (sut, db) = BuildService(nameof(CreateAsync_PreservesAiProvenance));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        await sut.CreateAsync(new CreateSummaryDto(
            note.Id, "AI summary", IsAiGenerated: true, Model: "gpt-4o-mini", PromptVersion: "v2"));

        var stored = await db.Summaries.AsNoTracking().SingleAsync();
        stored.IsAiGenerated.Should().BeTrue();
        stored.Model.Should().Be("gpt-4o-mini");
        stored.PromptVersion.Should().Be("v2");
    }

    [Fact]
    public async Task CreateAsync_WithBlankContent_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithBlankContent_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateSummaryDto(Guid.NewGuid(), "   "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreignNote = CreateNote(OtherUserId);
        db.Notes.Add(foreignNote);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateSummaryDto(foreignNote.Id, "content"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByNoteAsync_WhenNoteBelongsToAnotherUser_ReturnsEmpty()
    {
        var (sut, db) = BuildService(nameof(GetByNoteAsync_WhenNoteBelongsToAnotherUser_ReturnsEmpty));
        var foreignNote = CreateNote(OtherUserId);
        db.Notes.Add(foreignNote);
        db.Summaries.Add(new Summary { Id = Guid.NewGuid(), NoteId = foreignNote.Id, Content = "secret" });
        await db.SaveChangesAsync();

        var result = await sut.GetByNoteAsync(foreignNote.Id);

        result.Should().BeEmpty();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenSummaryExists_RemovesIt()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenSummaryExists_RemovesIt));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        var created = await sut.CreateAsync(new CreateSummaryDto(note.Id, "content"));

        await sut.DeleteAsync(created.Id);

        (await db.Summaries.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenSummaryBelongsToAnotherUsersNote_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenSummaryBelongsToAnotherUsersNote_ThrowsKeyNotFoundException));
        var foreignNote = CreateNote(OtherUserId);
        var summary = new Summary { Id = Guid.NewGuid(), NoteId = foreignNote.Id, Content = "secret" };
        db.Notes.Add(foreignNote);
        db.Summaries.Add(summary);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(summary.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
