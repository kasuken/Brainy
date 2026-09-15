using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.DTOs.Projects;
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
/// Unit tests for <see cref="IOutputTemplateService"/> resolved via the real DI
/// container with an EF Core InMemory database.
/// </summary>
public class OutputTemplateServiceTests
{
    private const string DefaultUserId = "output-template-user-1";

    private static (IOutputTemplateService Templates, IOutputService Outputs, INoteService Notes, IProjectService Projects, IAreaService Areas)
        BuildServices(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (
            sp.GetRequiredService<IOutputTemplateService>(),
            sp.GetRequiredService<IOutputService>(),
            sp.GetRequiredService<INoteService>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IAreaService>());
    }

    [Fact]
    public async Task InstantiateAsync_CreatesOutputWithTypeAndScaffoldFromTemplate()
    {
        var (templates, outputs, _, _, _) = BuildServices(nameof(InstantiateAsync_CreatesOutputWithTypeAndScaffoldFromTemplate));
        var template = await templates.CreateAsync(new CreateOutputTemplateDto(
            "Blog Draft", "Post — {Date}", OutputType.BlogPost, "## Hook\n"));

        var output = await templates.InstantiateAsync(new InstantiateOutputTemplateDto(template.Id));

        output.Type.Should().Be(OutputType.BlogPost);
        var stored = await outputs.GetDetailAsync(output.Id);
        stored!.Content.Should().Be("## Hook\n");
    }

    [Fact]
    public async Task InstantiateAsync_WithActiveProjectNotesSelection_PreSelectsProjectsActiveNotesAsSourceNotes()
    {
        var (templates, outputs, notes, projects, areas) = BuildServices(
            nameof(InstantiateAsync_WithActiveProjectNotesSelection_PreSelectsProjectsActiveNotesAsSourceNotes));

        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var project = await projects.CreateAsync(new CreateProjectDto("Project A", area.Id));
        var note = await notes.CreateAsync(new CreateNoteDto("Source note", "Body", ParaCategory.Project, ProjectId: project.Id));

        var template = await templates.CreateAsync(new CreateOutputTemplateDto(
            "Brief", "Brief — {Date}", OutputType.MeetingBrief, "## Purpose\n",
            OutputTemplateSourceSelectionMode.ActiveProjectNotes));

        var output = await templates.InstantiateAsync(new InstantiateOutputTemplateDto(template.Id, ProjectId: project.Id));

        var stored = await outputs.GetDetailAsync(output.Id);
        stored!.SourceNotes.Should().ContainSingle(n => n.NoteId == note.Id);
    }

    [Fact]
    public async Task CreateFromOutputAsync_CapturesTitleTypeAndContentAsTemplate()
    {
        var (templates, outputs, _, projects, areas) = BuildServices(nameof(CreateFromOutputAsync_CapturesTitleTypeAndContentAsTemplate));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var project = await projects.CreateAsync(new CreateProjectDto("Project A", area.Id));
        var output = await outputs.CreateAsync(new Brainy.Application.DTOs.Outputs.CreateOutputDto(
            "Q3 Report", null, OutputType.Report, "## Summary\n", ProjectId: project.Id));

        var template = await templates.CreateFromOutputAsync(new SaveOutputAsTemplateDto(output.Id, "Report Template"));

        template.Name.Should().Be("Report Template");
        template.TitlePattern.Should().Be("Q3 Report");
        template.Type.Should().Be(OutputType.Report);
        template.ContentScaffold.Should().Be("## Summary\n");
    }

    [Fact]
    public async Task GetAllAsync_WhenUserHasNoTemplates_SeedsBuiltInStarterSet()
    {
        var (templates, _, _, _, _) = BuildServices(nameof(GetAllAsync_WhenUserHasNoTemplates_SeedsBuiltInStarterSet));

        var all = await templates.GetAllAsync();

        all.Should().NotBeEmpty();
        all.Should().OnlyContain(t => t.IsBuiltIn);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTemplate()
    {
        var (templates, _, _, _, _) = BuildServices(nameof(DeleteAsync_RemovesTemplate));
        var template = await templates.CreateAsync(new CreateOutputTemplateDto("T", "Pattern", OutputType.Custom));

        await templates.DeleteAsync(template.Id);

        (await templates.GetByIdAsync(template.Id)).Should().BeNull();
    }
}
