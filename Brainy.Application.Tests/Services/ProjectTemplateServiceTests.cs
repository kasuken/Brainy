using Brainy.Application.Common;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.DTOs.Templates;
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
/// Unit tests for <see cref="IProjectTemplateService"/> resolved via the real DI
/// container with an EF Core InMemory database. Covers the acceptance criteria in
/// docs/roadmap/release-7/10-templates.md: instantiation creates tasks with correctly
/// offset due dates, saving an existing project as a template, and that instantiation
/// is subject to the normal active-project entitlement limit.
/// </summary>
public class ProjectTemplateServiceTests
{
    private const string DefaultUserId = "template-user-1";

    // Deterministic clock: due-date offsets resolve against this "today", never the
    // real calendar (see AGENTS.md — relative due dates use the user's calendar date).
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    private static (IProjectTemplateService Templates, IProjectService Projects, IAreaService Areas, ITaskService Tasks, BrainyDbContext Db)
        BuildServices(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (
            sp.GetRequiredService<IProjectTemplateService>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IAreaService>(),
            sp.GetRequiredService<ITaskService>(),
            sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task InstantiateAsync_CreatesProjectWithTasksAndCorrectlyOffsetDueDates()
    {
        var (templates, projects, areas, tasks, _) = BuildServices(
            nameof(InstantiateAsync_CreatesProjectWithTasksAndCorrectlyOffsetDueDates));

        var area = await areas.CreateAsync(new CreateAreaDto("Client Work"));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto(
            Name: "Kickoff",
            ProjectNamePattern: "Kickoff — {Date}",
            DefaultAreaId: area.Id,
            Tasks:
            [
                new CreateProjectTemplateTaskDto("Send agenda", DueDateOffsetDays: 0),
                new CreateProjectTemplateTaskDto("Hold call", DueDateOffsetDays: 2),
                new CreateProjectTemplateTaskDto("Follow up", DueDateOffsetDays: 7),
            ]));

        var project = await templates.InstantiateAsync(new InstantiateProjectTemplateDto(template.Id));

        project.AreaId.Should().Be(area.Id);
        project.Name.Should().Contain("Kickoff");

        var createdTasks = await tasks.GetByProjectAsync(project.Id);
        createdTasks.Should().HaveCount(3);
        createdTasks.Should().ContainSingle(t => t.Title == "Send agenda" && t.DueDate == Today);
        createdTasks.Should().ContainSingle(t => t.Title == "Hold call" && t.DueDate == Today.AddDays(2));
        createdTasks.Should().ContainSingle(t => t.Title == "Follow up" && t.DueDate == Today.AddDays(7));
    }

    [Fact]
    public async Task InstantiateAsync_WithNameOverride_UsesSuppliedNameInsteadOfPattern()
    {
        var (templates, _, areas, _, _) = BuildServices(nameof(InstantiateAsync_WithNameOverride_UsesSuppliedNameInsteadOfPattern));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto("T", "Default Name", DefaultAreaId: area.Id));

        var project = await templates.InstantiateAsync(new InstantiateProjectTemplateDto(template.Id, Name: "My Custom Name"));

        project.Name.Should().Be("My Custom Name");
    }

    [Fact]
    public async Task CreateFromProjectAsync_CapturesNameAreaAndTasksWithOffsetsRelativeToToday()
    {
        var (templates, projects, areas, tasks, _) = BuildServices(
            nameof(CreateFromProjectAsync_CapturesNameAreaAndTasksWithOffsetsRelativeToToday));

        var area = await areas.CreateAsync(new CreateAreaDto("Consulting"));
        var project = await projects.CreateAsync(new CreateProjectDto(
            "Acme Engagement", area.Id, Priority: ProjectPriority.High));
        await tasks.CreateAsync(new CreateTaskDto(project.Id, "Kickoff call", DueDate: Today.AddDays(3)));
        await tasks.CreateAsync(new CreateTaskDto(project.Id, "No due date task"));

        var template = await templates.CreateFromProjectAsync(new SaveProjectAsTemplateDto(project.Id, "Acme Playbook"));

        template.Name.Should().Be("Acme Playbook");
        template.ProjectNamePattern.Should().Be("Acme Engagement");
        template.DefaultAreaId.Should().Be(area.Id);
        template.DefaultPriority.Should().Be(ProjectPriority.High);
        template.Tasks.Should().ContainSingle(t => t.Title == "Kickoff call" && t.DueDateOffsetDays == 3);
        template.Tasks.Should().ContainSingle(t => t.Title == "No due date task" && t.DueDateOffsetDays == null);
    }

    [Fact]
    public async Task InstantiateAsync_WhenUserIsAtTheirActiveProjectLimit_ThrowsPlanEntitlementDeniedException()
    {
        var dbName = nameof(InstantiateAsync_WhenUserIsAtTheirActiveProjectLimit_ThrowsPlanEntitlementDeniedException);
        var (templates, projects, areas, _, _) = BuildServices(dbName);

        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        // Starter plan (the default with no UserPlan row) allows 3 active projects.
        await projects.CreateAsync(new CreateProjectDto("P1", area.Id));
        await projects.CreateAsync(new CreateProjectDto("P2", area.Id));
        await projects.CreateAsync(new CreateProjectDto("P3", area.Id));

        var template = await templates.CreateAsync(new CreateProjectTemplateDto("T", "From Template", DefaultAreaId: area.Id));

        var act = () => templates.InstantiateAsync(new InstantiateProjectTemplateDto(template.Id));

        await act.Should().ThrowAsync<PlanEntitlementDeniedException>();
        // Blocked at instantiation, not silently truncated: no project or its tasks exist.
        (await projects.GetAllNonArchivedAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task InstantiateAsync_WithoutAreaOverrideOrTemplateDefault_ThrowsArgumentException()
    {
        var (templates, _, _, _, _) = BuildServices(nameof(InstantiateAsync_WithoutAreaOverrideOrTemplateDefault_ThrowsArgumentException));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto("T", "No Area Template"));

        var act = () => templates.InstantiateAsync(new InstantiateProjectTemplateDto(template.Id));

        await act.Should().ThrowAsync<ArgumentException>();
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
    public async Task GetByIdAsync_ScopedToOwningUser_ReturnsNullForAnotherUsersTemplate()
    {
        var dbName = nameof(GetByIdAsync_ScopedToOwningUser_ReturnsNullForAnotherUsersTemplate);
        var (templatesUser1, _, areasUser1, _, _) = BuildServices(dbName, "owner");
        var (templatesUser2, _, _, _, _) = BuildServices(dbName, "other");

        var area = await areasUser1.CreateAsync(new CreateAreaDto("Work"));
        var template = await templatesUser1.CreateAsync(new CreateProjectTemplateDto("T", "Pattern", DefaultAreaId: area.Id));

        (await templatesUser2.GetByIdAsync(template.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTemplateAndItsTasks()
    {
        var (templates, _, areas, _, db) = BuildServices(nameof(DeleteAsync_RemovesTemplateAndItsTasks));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto(
            "T", "Pattern", DefaultAreaId: area.Id,
            Tasks: [new CreateProjectTemplateTaskDto("Task 1")]));

        await templates.DeleteAsync(template.Id);

        (await templates.GetByIdAsync(template.Id)).Should().BeNull();
        (await db.ProjectTemplateTasks.CountAsync(t => t.ProjectTemplateId == template.Id)).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTaskListWholesale()
    {
        var (templates, _, areas, _, _) = BuildServices(nameof(UpdateAsync_ReplacesTaskListWholesale));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto(
            "T", "Pattern", DefaultAreaId: area.Id,
            Tasks: [new CreateProjectTemplateTaskDto("Old task")]));

        var updated = await templates.UpdateAsync(new UpdateProjectTemplateDto(
            template.Id, "T Renamed", "Pattern", DefaultAreaId: area.Id,
            Tasks: [new CreateProjectTemplateTaskDto("New task")]));

        updated.Name.Should().Be("T Renamed");
        updated.Tasks.Should().ContainSingle(t => t.Title == "New task");
    }
}
