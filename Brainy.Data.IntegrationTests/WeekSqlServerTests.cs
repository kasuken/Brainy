using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Brainy.Data.IntegrationTests;

public sealed class WeekSqlServerTests
{
    [Fact]
    public async Task WeeklyTaskSelectionSchema_EnforcesUniqueConstraint_AndTaskDeleteCascades()
    {
        await using var fixture = await SqlServerWeekFixture.CreateAsync();
        await fixture.SeedAsync();

        fixture.Context.WeeklyTaskSelections.Add(new WeeklyTaskSelection
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            TaskId = fixture.PrimaryTaskId,
            WeekStartDate = new DateTime(2026, 6, 15)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());

        fixture.Context.ChangeTracker.Clear();
        var task = await fixture.Context.Tasks.SingleAsync(candidate => candidate.Id == fixture.PrimaryTaskId);
        fixture.Context.Tasks.Remove(task);
        await fixture.Context.SaveChangesAsync();

        Assert.False(await fixture.Context.WeeklyTaskSelections.AnyAsync(selection => selection.TaskId == fixture.PrimaryTaskId));
    }

    [Fact]
    public async Task WeekServiceQueries_TranslateSuccessfullyOnSqlServer()
    {
        await using var fixture = await SqlServerWeekFixture.CreateAsync();
        await fixture.SeedAsync();

        var overview = await fixture.WeekService.GetCurrentWeekOverviewAsync();
        var picker = await fixture.WeekService.GetSelectableTasksAsync(fixture.ProjectId);
        var carryForward = await fixture.WeekService.GetCarryForwardCandidatesAsync();

        Assert.Equal(new DateTime(2026, 6, 15), overview.WeekStartDate);
        Assert.Single(overview.SelectedTaskGroups);
        Assert.NotEmpty(picker.Tasks);
        Assert.Single(carryForward);
    }

    private sealed class SqlServerWeekFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly ServiceProvider _provider;

        private SqlServerWeekFixture(string masterConnectionString, string databaseName, ServiceProvider provider)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _provider = provider;
        }

        public string UserId { get; private set; } = Guid.NewGuid().ToString();
        public Guid ProjectId { get; private set; }
        public Guid PrimaryTaskId { get; private set; }
        public BrainyDbContext Context => _provider.GetRequiredService<BrainyDbContext>();
        public IWeekService WeekService => _provider.GetRequiredService<IWeekService>();

        public static async Task<SqlServerWeekFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
                configuredConnection = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server week tests.");

            var databaseName = $"BrainyWeek_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configuredConnection)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true
            };
            var application = new SqlConnectionStringBuilder(master.ConnectionString)
            {
                InitialCatalog = databaseName
            };

            try
            {
                await WaitForSqlAsync(master.ConnectionString);
                await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");
            }
            catch (Exception ex) when (!isExplicitlyConfigured && ex is SqlException or InvalidOperationException)
            {
                throw SkipException.ForSkip("SQL Server LocalDB is unavailable and BRAINY_TEST_SQL_CONNECTIONSTRING is not set.");
            }

            var userId = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(userId));
            services.AddSingleton<TimeProvider>(new TimeProviderStub(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero)));
            services.AddBrainyApplication();
            services.AddSingleton<IUserTimeZoneService>(new FixedUserTimeZoneService(new DateTime(2026, 6, 17)));

            var provider = services.BuildServiceProvider();
            var context = provider.GetRequiredService<BrainyDbContext>();
            await context.Database.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            var fixture = new SqlServerWeekFixture(master.ConnectionString, databaseName, provider)
            {
                UserId = userId
            };

            context.Users.Add(new ApplicationUser
            {
                Id = fixture.UserId,
                UserName = "week@example.test",
                NormalizedUserName = "WEEK@EXAMPLE.TEST",
                Email = "week@example.test",
                NormalizedEmail = "WEEK@EXAMPLE.TEST"
            });
            await context.SaveChangesAsync();

            return fixture;
        }

        public async Task SeedAsync()
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Name = "Week project",
                Status = ProjectStatus.Active,
                Priority = ProjectPriority.High
            };
            var selectedTask = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = project,
                Title = "Selected task",
                Status = TaskItemStatus.InProgress
            };
            var dueTask = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = project,
                Title = "Due task",
                Status = TaskItemStatus.Todo,
                DueDate = new DateTime(2026, 6, 18)
            };
            var previousWeekTask = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = project,
                Title = "Previous week task",
                Status = TaskItemStatus.Todo
            };

            Context.AddRange(
                project,
                selectedTask,
                dueTask,
                previousWeekTask,
                new WeeklyTaskSelection
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Task = selectedTask,
                    WeekStartDate = new DateTime(2026, 6, 15)
                },
                new WeeklyTaskSelection
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Task = previousWeekTask,
                    WeekStartDate = new DateTime(2026, 6, 8)
                });
            await Context.SaveChangesAsync();

            ProjectId = project.Id;
            PrimaryTaskId = selectedTask.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await ExecuteMasterCommandAsync(
                _masterConnectionString,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
        }

        private static async Task WaitForSqlAsync(string connectionString)
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    await using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();
                    return;
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException)
                {
                    lastFailure = ex;
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            throw new InvalidOperationException("SQL Server did not become ready for week integration tests.", lastFailure);
        }

        private static async Task ExecuteMasterCommandAsync(string connectionString, string commandText)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedUserTimeZoneService(DateTime today) : IUserTimeZoneService
    {
        public Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
        public Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeZoneInfo.Utc);
        public Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today);
        public Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetTimeZoneOverrideIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetTimeZoneOverrideAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(DateTime localStartDate, DateTime localEndDate, CancellationToken cancellationToken = default)
            => Task.FromResult((localStartDate.Date, localEndDate.Date.AddDays(1)));
    }

    private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(userId);
        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(userId);
    }

    private sealed class TimeProviderStub(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
