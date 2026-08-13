using Brainy.Application.DTOs.ActionItems;
using Brainy.Application.Interfaces.AI;
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

public sealed class ActionItemServiceTests
{
    private const string UserId = "u1";
    private const string OtherUserId = "u2";

    private static (IActionItemService Sut, BrainyDbContext Db, RecordingAiAssistant Ai) BuildService(
        string databaseName,
        string userId = UserId,
        AiResult? extractionResult = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        var ai = new RecordingAiAssistant(extractionResult ?? new AiResult("[]", "test-model", "extract-v1", true));
        services.AddSingleton<IAiAssistant>(ai);
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IActionItemService>(),
            provider.GetRequiredService<BrainyDbContext>(),
            ai);
    }

    [Fact]
    public async Task Crud_IsScopedToCurrentUsersNoteAndAction()
    {
        var (sut, db, _) = BuildService(nameof(Crud_IsScopedToCurrentUsersNoteAndAction));
        var ownNote = CreateNote(UserId, "Own note");
        var foreignNote = CreateNote(OtherUserId, "Foreign note");
        var foreignAction = CreateAction(OtherUserId, foreignNote.Id, "Secret action");
        db.Notes.AddRange(ownNote, foreignNote);
        db.ActionItems.Add(foreignAction);
        await db.SaveChangesAsync();

        var created = await sut.CreateAsync(new CreateActionItemDto(ownNote.Id, "  Follow up  ", "  Details  "));
        var updated = await sut.UpdateAsync(new UpdateActionItemDto(
            created.Id, "Send follow-up", null, ActionItemStatus.Done, created.RowVersion));

        updated.Title.Should().Be("Send follow-up");
        updated.Description.Should().BeNull();
        updated.Status.Should().Be(ActionItemStatus.Done);
        (await sut.GetByNoteAsync(ownNote.Id)).Should().ContainSingle();

        await sut.Invoking(service => service.GetByNoteAsync(foreignNote.Id))
            .Should().ThrowAsync<KeyNotFoundException>();
        await sut.Invoking(service => service.DeleteAsync(foreignAction.Id))
            .Should().ThrowAsync<KeyNotFoundException>();

        await sut.DeleteAsync(created.Id);
        (await sut.GetByNoteAsync(ownNote.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_RejectsBlankTitleAndForeignNote()
    {
        var (sut, db, _) = BuildService(nameof(CreateAsync_RejectsBlankTitleAndForeignNote));
        var ownNote = CreateNote(UserId, "Own note");
        var foreignNote = CreateNote(OtherUserId, "Foreign note");
        db.Notes.AddRange(ownNote, foreignNote);
        await db.SaveChangesAsync();

        await sut.Invoking(service => service.CreateAsync(new CreateActionItemDto(ownNote.Id, "   ")))
            .Should().ThrowAsync<ArgumentException>();
        await sut.Invoking(service => service.CreateAsync(new CreateActionItemDto(foreignNote.Id, "Steal")))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ExtractFromNoteAsync_PersistsProvenanceAndSkipsDuplicateTitles()
    {
        var result = new AiResult(
            "[\"Call Alice\",\"call alice\",\"Draft the launch memo\"]",
            "gpt-test",
            "extract-actions-v7",
            true);
        var (sut, db, ai) = BuildService(
            nameof(ExtractFromNoteAsync_PersistsProvenanceAndSkipsDuplicateTitles),
            extractionResult: result);
        var note = CreateNote(UserId, "Meeting", "Alice owns launch follow-up.");
        db.Notes.Add(note);
        db.ActionItems.Add(CreateAction(UserId, note.Id, "Call Alice"));
        await db.SaveChangesAsync();

        var created = await sut.ExtractFromNoteAsync(note.Id);

        created.Should().ContainSingle().Which.Title.Should().Be("Draft the launch memo");
        created[0].IsAiGenerated.Should().BeTrue();
        created[0].Model.Should().Be("gpt-test");
        created[0].PromptVersion.Should().Be("extract-actions-v7");
        ai.LastExtractedContent.Should().Be(note.Content);
        (await db.ActionItems.CountAsync(item => item.NoteId == note.Id)).Should().Be(2);
    }

    [Fact]
    public async Task ExtractFromNoteAsync_InvalidJsonDoesNotPersistPartialActions()
    {
        var result = new AiResult("not-json", "gpt-test", "extract-v1", true);
        var (sut, db, _) = BuildService(
            nameof(ExtractFromNoteAsync_InvalidJsonDoesNotPersistPartialActions),
            extractionResult: result);
        var note = CreateNote(UserId, "Meeting", "Follow up tomorrow.");
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        await sut.Invoking(service => service.ExtractFromNoteAsync(note.Id))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid action-item response*");
        (await db.ActionItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PromoteToTaskAsync_IsIdempotentForSameActiveProject()
    {
        var (sut, db, _) = BuildService(nameof(PromoteToTaskAsync_IsIdempotentForSameActiveProject));
        var note = CreateNote(UserId, "Meeting");
        var project = CreateProject(UserId, ProjectStatus.Active);
        var action = CreateAction(UserId, note.Id, "Prepare agenda");
        db.AddRange(note, project, action);
        await db.SaveChangesAsync();

        var first = await sut.PromoteToTaskAsync(action.Id, project.Id);
        var second = await sut.PromoteToTaskAsync(action.Id, project.Id);

        second.TaskItemId.Should().Be(first.TaskItemId);
        second.ProjectId.Should().Be(project.Id);
        (await db.Tasks.CountAsync()).Should().Be(1);
        var task = await db.Tasks.AsNoTracking().SingleAsync();
        task.UserId.Should().Be(UserId);
        task.Title.Should().Be(action.Title);
        task.ProjectId.Should().Be(project.Id);
    }

    [Theory]
    [InlineData(ProjectStatus.NotStarted, false)]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Active, true)]
    public async Task PromoteToTaskAsync_RequiresCurrentUsersActiveProject(
        ProjectStatus status,
        bool otherUser)
    {
        var databaseName = $"{nameof(PromoteToTaskAsync_RequiresCurrentUsersActiveProject)}-{status}-{otherUser}";
        var (sut, db, _) = BuildService(databaseName);
        var note = CreateNote(UserId, "Meeting");
        var action = CreateAction(UserId, note.Id, "Prepare agenda");
        var project = CreateProject(otherUser ? OtherUserId : UserId, status);
        db.AddRange(note, action, project);
        await db.SaveChangesAsync();

        await sut.Invoking(service => service.PromoteToTaskAsync(action.Id, project.Id))
            .Should().ThrowAsync<KeyNotFoundException>();
        (await db.Tasks.CountAsync()).Should().Be(0);
    }

    private static Note CreateNote(string userId, string title, string content = "Content") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = title,
        Content = content,
    };

    private static ActionItem CreateAction(string userId, Guid noteId, string title) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        NoteId = noteId,
        Title = title,
        Status = ActionItemStatus.Open,
    };

    private static Project CreateProject(string userId, ProjectStatus status) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = "Project",
        Status = status,
    };

    private sealed class RecordingAiAssistant(AiResult extractionResult) : IAiAssistant
    {
        public string? LastExtractedContent { get; private set; }

        public Task<AiResult> SummarizeAsync(string content, CancellationToken cancellationToken = default) =>
            Task.FromResult(AiResult.Failure("Not used."));

        public Task<AiResult> SuggestParaClassificationAsync(
            string title,
            string content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AiResult.Failure("Not used."));

        public Task<AiResult> ExtractActionItemsAsync(
            string content,
            CancellationToken cancellationToken = default)
        {
            LastExtractedContent = content;
            return Task.FromResult(extractionResult);
        }

        public Task<AiResult> DetectDuplicatesAsync(
            string noteTitle,
            string noteContent,
            IReadOnlyList<(Guid Id, string Title, string? Summary)> candidates,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AiResult.Failure("Not used."));

        public Task<AiResult> GenerateOutputAsync(
            string outputTitle,
            string outputType,
            IReadOnlyList<string> sourceNoteContents,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AiResult.Failure("Not used."));
    }
}
