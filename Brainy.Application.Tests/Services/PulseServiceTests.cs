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
/// Unit tests for <see cref="IPulseService"/> resolved via the real DI container with an
/// EF Core InMemory database. Each test uses a unique database name for isolation.
///
/// <see cref="BrainyDbContext"/> stamps <c>CreatedAtUtc</c> with the real system clock for
/// every newly-added entity (not the injected <see cref="TimeProvider"/>), so tests that need
/// a specific "captured/created" timestamp save the entity once, then mutate
/// <c>CreatedAtUtc</c> on the now-tracked entity and save again — that second save is an
/// <c>EntityState.Modified</c> change, which the context leaves untouched aside from
/// <c>UpdatedAtUtc</c>. Activity types with an explicit lifecycle timestamp (ProcessedAtUtc,
/// CompletedDate, ArchivedAtUtc, PublishedDate, CommittedAtUtc, AchievedDate) don't need this
/// trick since those properties are plain, unaudited fields.
/// </summary>
public class PulseServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    // Frozen "now" the service resolves preset period windows from. Unrelated to
    // CreatedAtUtc, which BrainyDbContext always stamps with the real system clock.
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static (IPulseService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IPulseService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    /// <summary>
    /// Re-saves an already-persisted entity with an explicit <c>CreatedAtUtc</c>. See the
    /// class remarks for why this two-step save is required to control that field in tests.
    /// </summary>
    private static async Task BackdateCreatedAtAsync(BrainyDbContext db, BaseEntity entity, DateTime createdAtUtc)
    {
        entity.CreatedAtUtc = createdAtUtc;
        await db.SaveChangesAsync();
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Note CreateNote(
        string userId,
        DateTime? processedAtUtc = null,
        bool isArchived = false,
        DateTime? archivedAtUtc = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Note",
            Content = "content",
            ProcessedAtUtc = processedAtUtc,
            IsArchived = isArchived,
            ArchivedAtUtc = archivedAtUtc
        };

    private static Project CreateProject(
        string userId,
        ProjectStatus status = ProjectStatus.Active,
        DateTime? completedDate = null,
        bool isArchived = false,
        DateTime? archivedAtUtc = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Project",
            Status = status,
            CompletedDate = completedDate,
            IsArchived = isArchived,
            ArchivedAtUtc = archivedAtUtc
        };

    private static TaskItem CreateTask(
        string userId,
        Project project,
        TaskItemStatus status = TaskItemStatus.Todo,
        DateTime? completedDate = null,
        bool isArchived = false,
        DateTime? archivedAtUtc = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = project.Id,
            Project = project,
            Title = "Task",
            Status = status,
            CompletedDate = completedDate,
            IsArchived = isArchived,
            ArchivedAtUtc = archivedAtUtc
        };

    private static Output CreateOutput(
        string userId,
        OutputStatus status = OutputStatus.Draft,
        DateTime? publishedDate = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Output",
            Content = "content",
            Status = status,
            PublishedDate = publishedDate
        };

    private static Idea CreateIdea(string userId, DateTime? committedAtUtc = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Idea",
            CommittedAtUtc = committedAtUtc
        };

    private static Goal CreateGoal(
        string userId,
        GoalStatus status = GoalStatus.Planned,
        DateTime? achievedDate = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Goal",
            Status = status,
            AchievedDate = achievedDate
        };

    // ── Empty state ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_WithNoActivity_ReturnsEmptyReport()
    {
        var (sut, _) = BuildService(nameof(GetReportAsync_WithNoActivity_ReturnsEmptyReport));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.TotalActivities.Should().Be(0);
        result.Summary.ActiveDays.Should().Be(0);
        result.Log.Should().BeEmpty();
    }

    // ── Per-activity-type coverage ───────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_CountsNoteCaptured()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsNoteCaptured));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, note, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesCaptured.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.NoteCaptured && e.Link == $"/notes/{note.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsNoteProcessed()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsNoteProcessed));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsNoteArchived()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsNoteArchived));
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: true, archivedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesArchived.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsHighlightAdded()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsHighlightAdded));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var highlight = new Highlight { Id = Guid.NewGuid(), NoteId = note.Id, Note = note, Text = "important" };
        db.Highlights.Add(highlight);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, highlight, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.HighlightsAdded.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.HighlightAdded && e.Link == $"/notes/{note.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsSummaryCreated()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsSummaryCreated));
        var note = CreateNote(DefaultUserId);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var summary = new Summary { Id = Guid.NewGuid(), NoteId = note.Id, Note = note, Content = "tl;dr" };
        db.Summaries.Add(summary);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, summary, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.SummariesCreated.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsTaskCreated()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsTaskCreated));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var task = CreateTask(DefaultUserId, project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, task, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.TasksCreated.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.TaskCreated && e.Link == $"/projects/{project.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsTaskCompleted()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsTaskCompleted));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.Tasks.Add(CreateTask(
            DefaultUserId, project, status: TaskItemStatus.Done, completedDate: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.TasksCompleted.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsTaskArchived()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsTaskArchived));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.Tasks.Add(CreateTask(
            DefaultUserId, project, isArchived: true, archivedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.TasksArchived.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsProjectCreated()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsProjectCreated));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, project, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.ProjectsCreated.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.ProjectCreated && e.Link == $"/projects/{project.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsProjectCompleted()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsProjectCompleted));
        db.Projects.Add(CreateProject(
            DefaultUserId, status: ProjectStatus.Completed, completedDate: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.ProjectsCompleted.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsProjectArchived()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsProjectArchived));
        db.Projects.Add(CreateProject(
            DefaultUserId, isArchived: true, archivedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.ProjectsArchived.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsOutputCreated()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsOutputCreated));
        var output = CreateOutput(DefaultUserId);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, output, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.OutputsCreated.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.OutputCreated && e.Link == $"/outputs/{output.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsOutputPublished()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsOutputPublished));
        db.Outputs.Add(CreateOutput(
            DefaultUserId, status: OutputStatus.Published, publishedDate: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.OutputsPublished.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsIdeaCaptured()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsIdeaCaptured));
        var idea = CreateIdea(DefaultUserId);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();
        await BackdateCreatedAtAsync(db, idea, FixedNow.UtcDateTime.AddDays(-1));

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.IdeasCaptured.Should().Be(1);
        result.Log.Should().ContainSingle(e =>
            e.ActivityType == PulseActivityType.IdeaCaptured && e.Link == $"/ideas/{idea.Id}");
    }

    [Fact]
    public async Task GetReportAsync_CountsIdeaCommitted()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsIdeaCommitted));
        db.Ideas.Add(CreateIdea(DefaultUserId, committedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.IdeasCommitted.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CountsGoalAchieved()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CountsGoalAchieved));
        db.Goals.Add(CreateGoal(
            DefaultUserId, status: GoalStatus.Achieved, achievedDate: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.GoalsAchieved.Should().Be(1);
    }

    // ── Range boundaries & presets ────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_ExcludesActivityBeforePeriodStart()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_ExcludesActivityBeforePeriodStart));
        // LastWeek's window starts 2026-06-09; 10 days before FixedNow lands well outside it.
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-10)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesProcessed.Should().Be(0);
    }

    [Fact]
    public async Task GetReportAsync_IncludesActivityOnPeriodStartBoundary()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_IncludesActivityOnPeriodStartBoundary));
        // LastWeek is a rolling 7-day window ending at the start of "tomorrow" relative to
        // FixedNow, so its start boundary is exactly (today + 1 day) - 7 days.
        var periodStart = FixedNow.UtcDateTime.Date.AddDays(1).AddDays(-7);
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: periodStart));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_LastMonthWindowIsWiderThanLastWeek()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_LastMonthWindowIsWiderThanLastWeek));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-20)));
        await db.SaveChangesAsync();

        var lastWeek = await sut.GetReportAsync(PulsePeriod.LastWeek);
        var lastMonth = await sut.GetReportAsync(PulsePeriod.LastMonth);

        lastWeek.Summary.NotesProcessed.Should().Be(0);
        lastMonth.Summary.NotesProcessed.Should().Be(1);
    }

    // ── Custom period ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_CustomPeriod_FiltersByGivenDates()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CustomPeriod_FiltersByGivenDates));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: new DateTime(2026, 1, 15)));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: new DateTime(2026, 2, 15)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(
            PulsePeriod.Custom, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        result.Summary.NotesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CustomPeriod_EndDateIsInclusive()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_CustomPeriod_EndDateIsInclusive));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: new DateTime(2026, 1, 31, 23, 0, 0)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(
            PulsePeriod.Custom, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        result.Summary.NotesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_CustomPeriod_MissingDates_Throws()
    {
        var (sut, _) = BuildService(nameof(GetReportAsync_CustomPeriod_MissingDates_Throws));

        var act = async () => await sut.GetReportAsync(PulsePeriod.Custom);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetReportAsync_CustomPeriod_EndBeforeStart_Throws()
    {
        var (sut, _) = BuildService(nameof(GetReportAsync_CustomPeriod_EndBeforeStart_Throws));

        var act = async () => await sut.GetReportAsync(
            PulsePeriod.Custom, new DateTime(2026, 2, 1), new DateTime(2026, 1, 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Isolation & aggregates ───────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_ExcludesOtherUsersActivity()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_ExcludesOtherUsersActivity));
        db.Notes.Add(CreateNote(OtherUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.NotesProcessed.Should().Be(0);
        result.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReportAsync_ComputesTotalActivitiesAndActiveDays()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_ComputesTotalActivitiesAndActiveDays));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-3)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Summary.TotalActivities.Should().Be(3);
        result.Summary.ActiveDays.Should().Be(2);
    }

    [Fact]
    public async Task GetReportAsync_OrdersLogMostRecentFirst()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_OrdersLogMostRecentFirst));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-3)));
        db.Notes.Add(CreateNote(DefaultUserId, processedAtUtc: FixedNow.UtcDateTime.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Log.Should().BeInDescendingOrder(e => e.OccurredAtUtc);
    }
}
