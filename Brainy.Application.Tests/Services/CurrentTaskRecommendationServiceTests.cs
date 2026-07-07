using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ICurrentTaskRecommendationService"/> resolved via the real DI
/// container with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class CurrentTaskRecommendationServiceTests
{
    private const string DefaultUserId = "u1";

    // Deterministic clock: the service resolves "today" from this anchor, so tests
    // never race the real calendar (midnight/time-zone boundaries).
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    private static (ICurrentTaskRecommendationService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (
            sp.GetRequiredService<ICurrentTaskRecommendationService>(),
            sp.GetRequiredService<BrainyDbContext>());
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Project CreateProject(string userId, bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Project",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.Medium,
            IsArchived = isArchived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static TaskItem CreateTask(
        Guid projectId,
        string userId,
        DateTime? dueDate = null,
        TaskItemStatus status = TaskItemStatus.Todo,
        TaskPriority priority = TaskPriority.Medium,
        bool isArchived = false,
        Guid? parentTaskId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Task",
            Status = status,
            Priority = priority,
            DueDate = dueDate,
            IsArchived = isArchived,
            ParentTaskId = parentTaskId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRecommendationAsync_WithNoEligibleTasks_ReturnsNull()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(GetRecommendationAsync_WithNoEligibleTasks_ReturnsNull));

        // Act
        var result = await sut.GetRecommendationAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecommendationAsync_WithOverdueCriticalTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetRecommendationAsync_WithOverdueCriticalTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);

        // Overdue critical = score 100
        var overdueCritical = CreateTask(project.Id, DefaultUserId,
            dueDate: Today.AddDays(-2),
            priority: TaskPriority.Critical);

        // Future medium = score 10
        var futureMedium = CreateTask(project.Id, DefaultUserId,
            dueDate: Today.AddDays(10),
            priority: TaskPriority.Medium);

        db.Projects.Add(project);
        db.Tasks.AddRange(overdueCritical, futureMedium);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetRecommendationAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(overdueCritical.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_WithDueTodayHighTask_ReturnsOverOverdueMedium()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetRecommendationAsync_WithDueTodayHighTask_ReturnsOverOverdueMedium));
        var project = CreateProject(DefaultUserId);

        // Due-today high = score 60
        var dueTodayHigh = CreateTask(project.Id, DefaultUserId,
            dueDate: Today,
            priority: TaskPriority.High);

        // Overdue medium = score 50
        var overdueMedium = CreateTask(project.Id, DefaultUserId,
            dueDate: Today.AddDays(-1),
            priority: TaskPriority.Medium);

        db.Projects.Add(project);
        db.Tasks.AddRange(dueTodayHigh, overdueMedium);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetRecommendationAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(dueTodayHigh.Id);
    }

    [Fact]
    public async Task GetRecommendationAsync_ExcludesArchivedTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetRecommendationAsync_ExcludesArchivedTasks));
        var project = CreateProject(DefaultUserId);
        var archivedTask = CreateTask(project.Id, DefaultUserId,
            dueDate: Today.AddDays(-1),
            priority: TaskPriority.Critical,
            isArchived: true);

        db.Projects.Add(project);
        db.Tasks.Add(archivedTask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetRecommendationAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecommendationAsync_ExcludesSubtasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetRecommendationAsync_ExcludesSubtasks));
        var project = CreateProject(DefaultUserId);

        // Parent task
        var parentTask = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Critical);

        // Subtask: should be excluded because ParentTaskId is set
        var subtask = CreateTask(project.Id, DefaultUserId,
            dueDate: Today.AddDays(-1),
            priority: TaskPriority.Critical,
            parentTaskId: parentTask.Id);

        db.Projects.Add(project);
        db.Tasks.AddRange(parentTask, subtask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetRecommendationAsync();

        // Assert — only the parent (no due date, scores 10) should be considered; subtask excluded
        result.Should().NotBeNull();
        result!.Id.Should().Be(parentTask.Id);
    }
}
