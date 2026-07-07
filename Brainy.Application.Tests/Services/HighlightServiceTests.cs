using Brainy.Application.DTOs.Highlights;
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
/// Unit tests for <see cref="IHighlightService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Highlights are owned through their parent note.
/// </summary>
public class HighlightServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IHighlightService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IHighlightService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Note CreateNote(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = "Note" };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithOwnedNote_PersistsTrimmedHighlight()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithOwnedNote_PersistsTrimmedHighlight));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateHighlightDto(note.Id, "  key insight  ", "  why it matters  ", Layer: 2));

        var stored = await db.Highlights.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.Text.Should().Be("key insight");
        stored.Annotation.Should().Be("why it matters");
        stored.Layer.Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_WithBlankText_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithBlankText_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateHighlightDto(Guid.NewGuid(), "   ", null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreignNote = CreateNote(OtherUserId);
        db.Notes.Add(foreignNote);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateHighlightDto(foreignNote.Id, "text", null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByNoteAsync_OrdersByLayerThenCreation()
    {
        var (sut, db) = BuildService(nameof(GetByNoteAsync_OrdersByLayerThenCreation));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        await sut.CreateAsync(new CreateHighlightDto(note.Id, "layer two", null, Layer: 2));
        await sut.CreateAsync(new CreateHighlightDto(note.Id, "layer one", null, Layer: 1));

        var result = await sut.GetByNoteAsync(note.Id);

        result.Select(h => h.Text).Should().ContainInOrder("layer one", "layer two");
    }

    [Fact]
    public async Task GetByNoteAsync_WhenNoteBelongsToAnotherUser_ReturnsEmpty()
    {
        var (sut, db) = BuildService(nameof(GetByNoteAsync_WhenNoteBelongsToAnotherUser_ReturnsEmpty));
        var foreignNote = CreateNote(OtherUserId);
        db.Notes.Add(foreignNote);
        db.Highlights.Add(new Highlight { Id = Guid.NewGuid(), NoteId = foreignNote.Id, Text = "secret" });
        await db.SaveChangesAsync();

        var result = await sut.GetByNoteAsync(foreignNote.Id);

        result.Should().BeEmpty();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesAnnotationAndLayer()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_ChangesAnnotationAndLayer));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        var created = await sut.CreateAsync(new CreateHighlightDto(note.Id, "text", "old", Layer: 1));

        await sut.UpdateAsync(created.Id, new UpdateHighlightDto("new", Layer: 3));

        var stored = await db.Highlights.AsNoTracking().SingleAsync();
        stored.Annotation.Should().Be("new");
        stored.Layer.Should().Be(3);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenHighlightExists_RemovesIt()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenHighlightExists_RemovesIt));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        var created = await sut.CreateAsync(new CreateHighlightDto(note.Id, "text", null));

        await sut.DeleteAsync(created.Id);

        (await db.Highlights.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenHighlightBelongsToAnotherUsersNote_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenHighlightBelongsToAnotherUsersNote_ThrowsKeyNotFoundException));
        var foreignNote = CreateNote(OtherUserId);
        var highlight = new Highlight { Id = Guid.NewGuid(), NoteId = foreignNote.Id, Text = "secret" };
        db.Notes.Add(foreignNote);
        db.Highlights.Add(highlight);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(highlight.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
