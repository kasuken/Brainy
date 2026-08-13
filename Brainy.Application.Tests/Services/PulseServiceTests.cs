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
/// Unit tests for <see cref="IPulseService"/> resolved via the real DI container with an
/// EF Core InMemory database. Each test uses a unique database name for isolation.
///
/// <see cref="BrainyDbContext"/> stamps <c>CreatedAtUtc</c> with the real system clock and
/// appends the matching immutable lifecycle row in the same save, so the frozen clock used
/// for period boundaries stays close to the real clock in creation-event tests.
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
            Status = committedAtUtc.HasValue ? IdeaStatus.Committed : IdeaStatus.Captured,
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

    private static LifecycleActivity CreateActivity(
        PulseActivityType activityType,
        DateTime occurredAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            EntityId = Guid.NewGuid(),
            ActivityType = activityType,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            Title = activityType.ToString(),
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

        var highlightId = Guid.NewGuid();
        db.LifecycleActivities.Add(new LifecycleActivity
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            EntityId = highlightId,
            ActivityType = PulseActivityType.HighlightAdded,
            OccurredAtUtc = FixedNow.UtcDateTime.AddDays(-1),
            Title = note.Title,
            Context = "Highlight added",
            Link = $"/notes/{note.Id}",
        });
        await db.SaveChangesAsync();

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

        db.LifecycleActivities.Add(new LifecycleActivity
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            EntityId = Guid.NewGuid(),
            ActivityType = PulseActivityType.SummaryCreated,
            OccurredAtUtc = FixedNow.UtcDateTime.AddDays(-1),
            Title = note.Title,
            Context = "Summary added",
            Link = $"/notes/{note.Id}",
        });
        await db.SaveChangesAsync();

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
        // Ten days before the frozen current time lands outside LastWeek.
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

        // Each newly captured note is itself an activity in addition to processing it.
        result.Summary.TotalActivities.Should().Be(6);
        result.Summary.ActiveDays.Should().Be(3);
    }

    [Fact]
    public async Task GetReportAsync_BuildsDailyMomentumForEveryDayInPeriod()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_BuildsDailyMomentumForEveryDayInPeriod));
        db.LifecycleActivities.AddRange(
            CreateActivity(PulseActivityType.NoteCaptured, new DateTime(2026, 6, 10, 9, 0, 0)),
            CreateActivity(PulseActivityType.NoteProcessed, new DateTime(2026, 6, 10, 10, 0, 0)),
            CreateActivity(PulseActivityType.HighlightAdded, new DateTime(2026, 6, 10, 11, 0, 0)),
            CreateActivity(PulseActivityType.TaskCompleted, new DateTime(2026, 6, 12, 16, 0, 0)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Analytics.DailyActivity.Should().HaveCount(7);
        result.Analytics.DailyActivity.Sum(day => day.TotalActivities).Should().Be(4);
        result.Analytics.DailyActivity.Sum(day => day.ForwardProgressActivities).Should().Be(3);
        result.Analytics.BusiestDate.Should().Be(new DateOnly(2026, 6, 10));
        result.Analytics.BusiestDateActivities.Should().Be(3);
    }

    [Fact]
    public async Task GetReportAsync_ClassifiesActivityMixAndForwardProgress()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_ClassifiesActivityMixAndForwardProgress));
        db.LifecycleActivities.AddRange(
            CreateActivity(PulseActivityType.NoteCaptured, new DateTime(2026, 6, 10, 8, 0, 0)),
            CreateActivity(PulseActivityType.NoteProcessed, new DateTime(2026, 6, 10, 9, 0, 0)),
            CreateActivity(PulseActivityType.SummaryCreated, new DateTime(2026, 6, 10, 10, 0, 0)),
            CreateActivity(PulseActivityType.OutputPublished, new DateTime(2026, 6, 10, 11, 0, 0)),
            CreateActivity(PulseActivityType.TaskArchived, new DateTime(2026, 6, 10, 12, 0, 0)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Analytics.ActivityMix.Should().ContainSingle(bucket =>
            bucket.Label == "Capture" && bucket.Value == 1);
        result.Analytics.ActivityMix.Should().ContainSingle(bucket =>
            bucket.Label == "Organize" && bucket.Value == 1);
        result.Analytics.ActivityMix.Should().ContainSingle(bucket =>
            bucket.Label == "Distill" && bucket.Value == 1);
        result.Analytics.ActivityMix.Should().ContainSingle(bucket =>
            bucket.Label == "Execute & express" && bucket.Value == 1);
        result.Analytics.ActivityMix.Should().ContainSingle(bucket =>
            bucket.Label == "Maintenance" && bucket.Value == 1);
        result.Analytics.ForwardProgressActivities.Should().Be(3);
        result.Analytics.ForwardProgressPercentage.Should().Be(60);
    }

    [Fact]
    public async Task GetReportAsync_IdentifiesProductiveWeekdayAndTimeBlock()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_IdentifiesProductiveWeekdayAndTimeBlock));
        db.LifecycleActivities.AddRange(
            CreateActivity(PulseActivityType.NoteProcessed, new DateTime(2026, 6, 10, 8, 0, 0)),
            CreateActivity(PulseActivityType.SummaryCreated, new DateTime(2026, 6, 10, 10, 0, 0)),
            CreateActivity(PulseActivityType.TaskCompleted, new DateTime(2026, 6, 10, 11, 0, 0)),
            CreateActivity(PulseActivityType.NoteCaptured, new DateTime(2026, 6, 10, 14, 0, 0)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Analytics.MostProductiveWeekday.Should().Be("Wed");
        result.Analytics.MostProductiveTimeBlock.Should().Be("8-11 AM");
        result.Analytics.ProductiveTimeBlocks.Should().ContainSingle(bucket =>
            bucket.Label == "8-11 AM" && bucket.Value == 3);
    }

    [Fact]
    public async Task GetReportAsync_UsesUserTimeZoneForProductivityPatterns()
    {
        var (sut, db) = BuildService(nameof(GetReportAsync_UsesUserTimeZoneForProductivityPatterns));
        db.DashboardPreferences.Add(new UserDashboardPreference
        {
            UserId = DefaultUserId,
            TimeZoneId = "Asia/Tokyo",
        });
        db.LifecycleActivities.Add(
            CreateActivity(PulseActivityType.TaskCompleted, new DateTime(2026, 6, 10, 23, 30, 0)));
        await db.SaveChangesAsync();

        var result = await sut.GetReportAsync(PulsePeriod.LastWeek);

        result.Analytics.DailyActivity.Should().ContainSingle(day =>
            day.Date == new DateOnly(2026, 6, 11) && day.ForwardProgressActivities == 1);
        result.Analytics.MostProductiveWeekday.Should().Be("Thu");
        result.Analytics.MostProductiveTimeBlock.Should().Be("8-11 AM");
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

    [Fact]
    public async Task LifecycleLedger_PreservesTaskCompletionAfterTaskIsReopened()
    {
        var (sut, db) = BuildService(nameof(LifecycleLedger_PreservesTaskCompletionAfterTaskIsReopened));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(DefaultUserId, project);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        task.Status = TaskItemStatus.Done;
        task.CompletedDate = FixedNow.UtcDateTime.AddDays(-1);
        await db.SaveChangesAsync();

        task.Status = TaskItemStatus.Todo;
        task.CompletedDate = null;
        await db.SaveChangesAsync();

        var activities = await db.LifecycleActivities.AsNoTracking()
            .Where(activity => activity.EntityId == task.Id)
            .Select(activity => activity.ActivityType)
            .ToListAsync();
        activities.Should().ContainSingle(type => type == PulseActivityType.TaskCompleted);
        activities.Should().ContainSingle(type => type == PulseActivityType.TaskReopened);

        var report = await sut.GetReportAsync(PulsePeriod.LastWeek);
        report.Summary.TasksCompleted.Should().Be(1);
        report.Log.Should().Contain(entry => entry.ActivityType == PulseActivityType.TaskReopened);
    }

    [Fact]
    public async Task LifecycleLedger_RecordsLifecycleTransitionsWithoutCollapsingHistory()
    {
        var (_, db) = BuildService(nameof(LifecycleLedger_RecordsLifecycleTransitionsWithoutCollapsingHistory));
        var note = CreateNote(DefaultUserId);
        var project = CreateProject(DefaultUserId);
        var output = CreateOutput(DefaultUserId);
        var idea = CreateIdea(DefaultUserId);
        db.AddRange(note, project, output, idea);
        await db.SaveChangesAsync();

        note.ProcessedAtUtc = FixedNow.UtcDateTime.AddMinutes(-4);
        note.Status = NoteStatus.Active;
        project.Status = ProjectStatus.Completed;
        project.CompletedDate = FixedNow.UtcDateTime.AddMinutes(-3);
        output.Status = OutputStatus.Published;
        output.PublishedDate = FixedNow.UtcDateTime.AddMinutes(-2);
        idea.Status = IdeaStatus.Committed;
        idea.CommittedAtUtc = FixedNow.UtcDateTime.AddMinutes(-1);
        await db.SaveChangesAsync();

        note.IsArchived = true;
        note.ArchivedAtUtc = FixedNow.UtcDateTime;
        note.Status = NoteStatus.Archived;
        project.IsArchived = true;
        project.ArchivedAtUtc = FixedNow.UtcDateTime;
        project.Status = ProjectStatus.Archived;
        output.IsArchived = true;
        output.ArchivedDate = FixedNow.UtcDateTime;
        output.Status = OutputStatus.Archived;
        await db.SaveChangesAsync();

        note.IsArchived = false;
        note.ArchivedAtUtc = null;
        note.Status = NoteStatus.Active;
        project.IsArchived = false;
        project.ArchivedAtUtc = null;
        project.Status = ProjectStatus.Completed;
        output.IsArchived = false;
        output.ArchivedDate = null;
        output.Status = OutputStatus.Draft;
        await db.SaveChangesAsync();

        var activities = await db.LifecycleActivities.AsNoTracking().ToListAsync();
        activities.Should().ContainSingle(a => a.EntityId == note.Id && a.ActivityType == PulseActivityType.NoteProcessed);
        activities.Should().ContainSingle(a => a.EntityId == note.Id && a.ActivityType == PulseActivityType.NoteArchived);
        activities.Should().ContainSingle(a => a.EntityId == note.Id && a.ActivityType == PulseActivityType.NoteRestored);
        activities.Should().ContainSingle(a => a.EntityId == project.Id && a.ActivityType == PulseActivityType.ProjectCompleted);
        activities.Should().ContainSingle(a => a.EntityId == project.Id && a.ActivityType == PulseActivityType.ProjectArchived);
        activities.Should().ContainSingle(a => a.EntityId == project.Id && a.ActivityType == PulseActivityType.ProjectRestored);
        activities.Should().ContainSingle(a => a.EntityId == output.Id && a.ActivityType == PulseActivityType.OutputPublished);
        activities.Should().ContainSingle(a => a.EntityId == output.Id && a.ActivityType == PulseActivityType.OutputArchived);
        activities.Should().ContainSingle(a => a.EntityId == output.Id && a.ActivityType == PulseActivityType.OutputRestored);
        activities.Should().ContainSingle(a => a.EntityId == idea.Id && a.ActivityType == PulseActivityType.IdeaCommitted);
    }

    [Fact]
    public async Task LifecycleLedger_RejectsMutationOfExistingActivity()
    {
        var (_, db) = BuildService(nameof(LifecycleLedger_RejectsMutationOfExistingActivity));
        db.Notes.Add(CreateNote(DefaultUserId));
        await db.SaveChangesAsync();
        var activity = await db.LifecycleActivities.SingleAsync();
        activity.Title = "rewritten history";

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }
}
