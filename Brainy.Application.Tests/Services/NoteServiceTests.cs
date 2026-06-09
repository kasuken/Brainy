using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Services;
using Brainy.Data;
using Brainy.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="INoteService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class NoteServiceTests
{
    private static INoteService BuildService(string dbName)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());

        services.AddBrainyApplication();

        return services.BuildServiceProvider().GetRequiredService<INoteService>();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId()
    {
        var sut = BuildService(nameof(CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId));

        var result = await sut.CreateAsync(new CreateNoteDto("My first note", "Some content", ParaCategory.Area));

        result.Id.Should().NotBeEmpty();
        result.Title.Should().Be("My first note");
        result.Content.Should().Be("Some content");
        result.ParaCategory.Should().Be(ParaCategory.Area);
        result.Status.Should().Be(NoteStatus.Inbox);
        result.AiSummary.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_PersistsNoteToDatabase()
    {
        var dbName = nameof(CreateAsync_PersistsNoteToDatabase);
        var sut = BuildService(dbName);

        await sut.CreateAsync(new CreateNoteDto("Persisted note"));

        // Verify directly via a fresh context on the same named DB.
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var sut = BuildService(nameof(CreateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Read (single) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenNoteExists_ReturnsCorrectDto()
    {
        var sut = BuildService(nameof(GetByIdAsync_WhenNoteExists_ReturnsCorrectDto));
        var created = await sut.CreateAsync(new CreateNoteDto("Find me", "body", ParaCategory.Resource));

        var result = await sut.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Find me");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNoteDoesNotExist_ReturnsNull()
    {
        var sut = BuildService(nameof(GetByIdAsync_WhenNoteDoesNotExist_ReturnsNull));

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── Read (list) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_WhenNoNotes_ReturnsEmptyList()
    {
        var sut = BuildService(nameof(GetAllAsync_WhenNoNotes_ReturnsEmptyList));

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllNotes()
    {
        var sut = BuildService(nameof(GetAllAsync_ReturnsAllNotes));
        await sut.CreateAsync(new CreateNoteDto("Note A"));
        await sut.CreateAsync(new CreateNoteDto("Note B"));
        await sut.CreateAsync(new CreateNoteDto("Note C"));

        var result = await sut.GetAllAsync();

        result.Should().HaveCount(3);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithValidDto_UpdatesAllFields()
    {
        var sut = BuildService(nameof(UpdateAsync_WithValidDto_UpdatesAllFields));
        var created = await sut.CreateAsync(new CreateNoteDto("Original"));

        var updated = await sut.UpdateAsync(new UpdateNoteDto(
            created.Id, "Updated title", "Updated content", "AI summary text",
            NoteStatus.Distilled, ParaCategory.Archive,
            null, null, null));

        updated.Title.Should().Be("Updated title");
        updated.Content.Should().Be("Updated content");
        updated.AiSummary.Should().Be("AI summary text");
        updated.Status.Should().Be(NoteStatus.Distilled);
        updated.ParaCategory.Should().Be(ParaCategory.Archive);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = BuildService(nameof(UpdateAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException));

        var act = () => sut.UpdateAsync(new UpdateNoteDto(
            Guid.NewGuid(), "Title", "Content", null,
            NoteStatus.Inbox, ParaCategory.Project,
            null, null, null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var sut = BuildService(nameof(UpdateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenNoteExists_RemovesFromDatabase()
    {
        var sut = BuildService(nameof(DeleteAsync_WhenNoteExists_RemovesFromDatabase));
        var created = await sut.CreateAsync(new CreateNoteDto("To delete"));

        await sut.DeleteAsync(created.Id);

        var result = await sut.GetByIdAsync(created.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = BuildService(nameof(DeleteAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException));

        var act = () => sut.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
