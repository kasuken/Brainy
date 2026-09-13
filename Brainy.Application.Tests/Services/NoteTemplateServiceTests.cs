using Brainy.Application.DTOs.Templates;
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
/// Unit tests for <see cref="INoteTemplateService"/> resolved via the real DI
/// container with an EF Core InMemory database.
/// </summary>
public class NoteTemplateServiceTests
{
    private const string DefaultUserId = "note-template-user-1";

    private static (INoteTemplateService Templates, INoteService Notes) BuildServices(
        string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<INoteTemplateService>(), sp.GetRequiredService<INoteService>());
    }

    [Fact]
    public async Task InstantiateAsync_CreatesNoteWithTitleAndScaffoldFromTemplate()
    {
        var (templates, notes) = BuildServices(nameof(InstantiateAsync_CreatesNoteWithTitleAndScaffoldFromTemplate));
        var template = await templates.CreateAsync(new CreateNoteTemplateDto(
            "Meeting Notes", "Meeting — {Date}", "## Agenda\n", ParaCategory.Project));

        var note = await templates.InstantiateAsync(new InstantiateNoteTemplateDto(template.Id));

        note.Content.Should().Be("## Agenda\n");
        note.ParaCategory.Should().Be(ParaCategory.Project);

        var stored = await notes.GetByIdAsync(note.Id);
        stored.Should().NotBeNull();
        stored!.Content.Should().Be("## Agenda\n");
    }

    [Fact]
    public async Task InstantiateAsync_WithTitleOverride_UsesSuppliedTitle()
    {
        var (templates, _) = BuildServices(nameof(InstantiateAsync_WithTitleOverride_UsesSuppliedTitle));
        var template = await templates.CreateAsync(new CreateNoteTemplateDto("T", "Default Title"));

        var note = await templates.InstantiateAsync(new InstantiateNoteTemplateDto(template.Id, Title: "Custom Title"));

        note.Title.Should().Be("Custom Title");
    }

    [Fact]
    public async Task CreateFromNoteAsync_CapturesTitleAndContentAsTemplate()
    {
        var (templates, notes) = BuildServices(nameof(CreateFromNoteAsync_CapturesTitleAndContentAsTemplate));
        var note = await notes.CreateAsync(new Brainy.Application.DTOs.Notes.CreateNoteDto(
            "Weekly Sync", "## Notes\nBody text", ParaCategory.Area));

        var template = await templates.CreateFromNoteAsync(new SaveNoteAsTemplateDto(note.Id, "Weekly Sync Template"));

        template.Name.Should().Be("Weekly Sync Template");
        template.TitlePattern.Should().Be("Weekly Sync");
        template.ContentScaffold.Should().Be("## Notes\nBody text");
        template.DefaultParaCategory.Should().Be(ParaCategory.Area);
    }

    [Fact]
    public async Task GetAllAsync_WhenUserHasNoTemplates_SeedsBuiltInStarterSet()
    {
        var (templates, _) = BuildServices(nameof(GetAllAsync_WhenUserHasNoTemplates_SeedsBuiltInStarterSet));

        var all = await templates.GetAllAsync();

        all.Should().NotBeEmpty();
        all.Should().OnlyContain(t => t.IsBuiltIn);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTemplate()
    {
        var (templates, _) = BuildServices(nameof(DeleteAsync_RemovesTemplate));
        var template = await templates.CreateAsync(new CreateNoteTemplateDto("T", "Pattern"));

        await templates.DeleteAsync(template.Id);

        (await templates.GetByIdAsync(template.Id)).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ScopedToOwningUser_ReturnsNullForAnotherUsersTemplate()
    {
        var dbName = nameof(GetByIdAsync_ScopedToOwningUser_ReturnsNullForAnotherUsersTemplate);
        var (templatesOwner, _) = BuildServices(dbName, "owner");
        var (templatesOther, _) = BuildServices(dbName, "other");

        var template = await templatesOwner.CreateAsync(new CreateNoteTemplateDto("T", "Pattern"));

        (await templatesOther.GetByIdAsync(template.Id)).Should().BeNull();
    }
}
