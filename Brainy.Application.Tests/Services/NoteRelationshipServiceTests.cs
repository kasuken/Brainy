using Brainy.Application.DTOs.NoteRelationships;
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
/// Unit tests for <see cref="INoteRelationshipService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class NoteRelationshipServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (INoteRelationshipService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<INoteRelationshipService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Note CreateNote(string userId, string title)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = title };

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidNotes_PersistsOutgoingRelationship()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithValidNotes_PersistsOutgoingRelationship));
        var source = CreateNote(DefaultUserId, "Source");
        var target = CreateNote(DefaultUserId, "Target");
        db.Notes.AddRange(source, target);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateNoteRelationshipDto(
            source.Id, target.Id, RelationshipType.References, Annotation: "cites"));

        result.LinkedNoteId.Should().Be(target.Id);
        result.LinkedNoteTitle.Should().Be("Target");
        result.IsOutgoing.Should().BeTrue();
        result.IsAiGenerated.Should().BeFalse();
        (await db.NoteRelationships.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateAsync_WhenSourceEqualsTarget_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenSourceEqualsTarget_ThrowsInvalidOperationException));
        var note = CreateNote(DefaultUserId, "Self");
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateNoteRelationshipDto(
            note.Id, note.Id, RelationshipType.Related));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_WhenSourceNoteDoesNotExist_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenSourceNoteDoesNotExist_ThrowsKeyNotFoundException));
        var target = CreateNote(DefaultUserId, "Target");
        db.Notes.Add(target);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateNoteRelationshipDto(
            Guid.NewGuid(), target.Id, RelationshipType.Related));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_WhenTargetNoteBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenTargetNoteBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var source = CreateNote(DefaultUserId, "Mine");
        var foreignTarget = CreateNote(OtherUserId, "Not mine");
        db.Notes.AddRange(source, foreignTarget);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateNoteRelationshipDto(
            source.Id, foreignTarget.Id, RelationshipType.Related));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_WhenSameDirectionAndTypeExists_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenSameDirectionAndTypeExists_ThrowsInvalidOperationException));
        var source = CreateNote(DefaultUserId, "Source");
        var target = CreateNote(DefaultUserId, "Target");
        db.Notes.AddRange(source, target);
        await db.SaveChangesAsync();
        await sut.CreateAsync(new CreateNoteRelationshipDto(source.Id, target.Id, RelationshipType.Related));

        var act = () => sut.CreateAsync(new CreateNoteRelationshipDto(
            source.Id, target.Id, RelationshipType.Related));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_WithDifferentTypeBetweenSameNotes_Succeeds()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithDifferentTypeBetweenSameNotes_Succeeds));
        var source = CreateNote(DefaultUserId, "Source");
        var target = CreateNote(DefaultUserId, "Target");
        db.Notes.AddRange(source, target);
        await db.SaveChangesAsync();
        await sut.CreateAsync(new CreateNoteRelationshipDto(source.Id, target.Id, RelationshipType.Related));

        await sut.CreateAsync(new CreateNoteRelationshipDto(source.Id, target.Id, RelationshipType.Supports));

        (await db.NoteRelationships.AsNoTracking().CountAsync()).Should().Be(2);
    }

    // ── GetForNoteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetForNoteAsync_ReturnsOutgoingAndIncomingRelationships()
    {
        var (sut, db) = BuildService(nameof(GetForNoteAsync_ReturnsOutgoingAndIncomingRelationships));
        var pivot = CreateNote(DefaultUserId, "Pivot");
        var linkedTo = CreateNote(DefaultUserId, "Linked to");
        var linkedFrom = CreateNote(DefaultUserId, "Linked from");
        db.Notes.AddRange(pivot, linkedTo, linkedFrom);
        await db.SaveChangesAsync();
        await sut.CreateAsync(new CreateNoteRelationshipDto(pivot.Id, linkedTo.Id, RelationshipType.References));
        await sut.CreateAsync(new CreateNoteRelationshipDto(linkedFrom.Id, pivot.Id, RelationshipType.FollowUp));

        var result = await sut.GetForNoteAsync(pivot.Id);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(r => r.IsOutgoing && r.LinkedNoteId == linkedTo.Id);
        result.Should().ContainSingle(r => !r.IsOutgoing && r.LinkedNoteId == linkedFrom.Id);
    }

    [Fact]
    public async Task GetForNoteAsync_DoesNotReturnAnotherUsersRelationships()
    {
        var (sut, db) = BuildService(nameof(GetForNoteAsync_DoesNotReturnAnotherUsersRelationships));
        var foreignSource = CreateNote(OtherUserId, "Foreign source");
        var foreignTarget = CreateNote(OtherUserId, "Foreign target");
        db.Notes.AddRange(foreignSource, foreignTarget);
        db.NoteRelationships.Add(new NoteRelationship
        {
            Id = Guid.NewGuid(),
            SourceNoteId = foreignSource.Id,
            TargetNoteId = foreignTarget.Id,
            Type = RelationshipType.Related
        });
        await db.SaveChangesAsync();

        var result = await sut.GetForNoteAsync(foreignSource.Id);

        result.Should().BeEmpty();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenRelationshipExists_RemovesIt()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenRelationshipExists_RemovesIt));
        var source = CreateNote(DefaultUserId, "Source");
        var target = CreateNote(DefaultUserId, "Target");
        db.Notes.AddRange(source, target);
        await db.SaveChangesAsync();
        var created = await sut.CreateAsync(new CreateNoteRelationshipDto(
            source.Id, target.Id, RelationshipType.Related));

        await sut.DeleteAsync(created.Id);

        (await db.NoteRelationships.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenRelationshipBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenRelationshipBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreignSource = CreateNote(OtherUserId, "Foreign source");
        var foreignTarget = CreateNote(OtherUserId, "Foreign target");
        var relationship = new NoteRelationship
        {
            Id = Guid.NewGuid(),
            SourceNoteId = foreignSource.Id,
            TargetNoteId = foreignTarget.Id,
            Type = RelationshipType.Related
        };
        db.Notes.AddRange(foreignSource, foreignTarget);
        db.NoteRelationships.Add(relationship);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(relationship.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
