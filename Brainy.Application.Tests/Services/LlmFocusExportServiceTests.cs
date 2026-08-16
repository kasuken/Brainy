using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Brainy.Application.AI.Prompts;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

public class LlmFocusExportServiceTests
{
    private const string DefaultUserId = "focus-user";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 16, 15, 52, 13, TimeSpan.Zero);

    private static (ILlmFocusExportService Sut, BrainyDbContext Db) BuildService(
        string databaseName,
        ICurrentUserService? currentUser = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton(currentUser ?? new FakeCurrentUserService(DefaultUserId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ILlmFocusExportService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ExportCurrentUserAsync_WithoutAuthenticatedUser_IsRejected()
    {
        var (sut, _) = BuildService(
            nameof(ExportCurrentUserAsync_WithoutAuthenticatedUser_IsRejected),
            new UnauthenticatedCurrentUserService());

        var act = () => sut.ExportCurrentUserAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ExportCurrentUserAsync_ExportsOnlyCurrentUsersActivePlanningContext()
    {
        var (sut, db) = BuildService(
            nameof(ExportCurrentUserAsync_ExportsOnlyCurrentUsersActivePlanningContext));
        var project = new Project
        {
            UserId = DefaultUserId,
            Name = "Launch focus",
            Description = "MY-CONTEXT",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.High,
            DueDate = new DateTime(2026, 8, 20)
        };
        var task = new TaskItem
        {
            UserId = DefaultUserId,
            Project = project,
            Title = "Current action",
            Status = TaskItemStatus.InProgress,
            Priority = TaskPriority.High,
            DueDate = new DateTime(2026, 8, 15),
            IsCurrentTask = true
        };
        var prerequisite = new TaskItem
        {
            UserId = DefaultUserId,
            Project = project,
            Title = "Required action",
            Status = TaskItemStatus.Waiting,
            Priority = TaskPriority.Medium
        };
        var foreignProject = new Project
        {
            UserId = "other-user",
            Name = "FOREIGN-SECRET",
            Status = ProjectStatus.Active
        };
        var archivedProject = new Project
        {
            UserId = DefaultUserId,
            Name = "ARCHIVED-CONTEXT",
            Status = ProjectStatus.Archived,
            IsArchived = true
        };
        var foreignTask = new TaskItem
        {
            UserId = "other-user",
            Project = foreignProject,
            Title = "FOREIGN-TASK",
            Status = TaskItemStatus.Todo
        };
        db.AddRange(
            project,
            task,
            prerequisite,
            new TaskDependency { Task = task, DependsOnTask = prerequisite },
            foreignProject,
            foreignTask,
            new TaskDependency { Task = task, DependsOnTask = foreignTask },
            archivedProject);
        await db.SaveChangesAsync();

        var export = await sut.ExportCurrentUserAsync();

        var json = Encoding.UTF8.GetString(export.Content);
        json.Should().Contain("MY-CONTEXT");
        json.Should().Contain("Current action");
        json.Should().Contain("Required action");
        json.Should().NotContain("FOREIGN-SECRET");
        json.Should().NotContain("FOREIGN-TASK");
        json.Should().NotContain("ARCHIVED-CONTEXT");
        json.Should().NotContain("\"userId\"");
        json.Should().NotContain("rowVersion");

        using var document = JsonDocument.Parse(export.Content);
        var root = document.RootElement;
        root.GetProperty("statusSummary").GetProperty("overdueTaskCount").GetInt32().Should().Be(1);
        root.GetProperty("statusSummary").GetProperty("waitingTaskCount").GetInt32().Should().Be(1);
        root.GetProperty("data").GetProperty("taskDependencies").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExportCurrentUserAsync_ProducesVersionedLlmReadyJson()
    {
        var (sut, _) = BuildService(nameof(ExportCurrentUserAsync_ProducesVersionedLlmReadyJson));

        var export = await sut.ExportCurrentUserAsync();

        export.FileName.Should().Be("brainy-focus-20260816-v1.0.json");
        export.ContentType.Should().Be("application/json;charset=utf-8");
        export.SchemaVersion.Should().Be(ILlmFocusExportService.SchemaVersion);
        export.PromptVersion.Should().Be(AiPrompts.FocusPlanningVersion);

        using var document = JsonDocument.Parse(export.Content);
        var root = document.RootElement;
        root.GetProperty("purpose").GetString().Should().Be("focus-planning");
        root.GetProperty("calendarDate").GetString().Should().Be("2026-08-16");
        root.GetProperty("timeZoneId").GetString().Should().Be("UTC");
        root.GetProperty("prompt").GetProperty("version").GetString()
            .Should().Be(AiPrompts.FocusPlanningVersion);
        root.GetProperty("prompt").GetProperty("text").GetString()
            .Should().Contain("Next 7 days");
        root.GetProperty("privacy").GetProperty("sentAutomatically").GetBoolean().Should().BeFalse();
    }

    private sealed class UnauthenticatedCurrentUserService : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new UnauthorizedAccessException());
    }
}
