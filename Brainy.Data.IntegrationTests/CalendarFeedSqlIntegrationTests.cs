using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.DTOs.Calendar;
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

/// <summary>
/// Verifies the calendar feed token/schema against a real SQL Server database — the unique
/// index that keeps exactly one token row per user, the real <c>rowversion</c> concurrency
/// column, and the migration that created the table — none of which EF InMemory can prove
/// (see AGENTS.md). Also proves, against real persisted data, the acceptance criterion that
/// one user's feed token never returns another user's events.
/// </summary>
public sealed class CalendarFeedSqlIntegrationTests
{
    private const string UserA = "calendar-feed-sql-user-a";
    private const string UserB = "calendar-feed-sql-user-b";

    [Fact]
    public async Task RegenerateAsync_PersistsOnlyAHash_NeverTheRawToken()
    {
        await using var fixture = await SqlServerCalendarFeedFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ICalendarFeedTokenService>();

        var result = await tokenService.RegenerateAsync();

        await using var readScope = fixture.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var row = await db.CalendarFeedTokens.AsNoTracking().SingleAsync(t => t.UserId == UserA);

        row.TokenHash.Should().NotBe(result.RawToken);
        row.TokenHash.Should().HaveLength(64);
        row.RowVersion.Should().NotBeNull("CalendarFeedToken derives from BaseEntity, which gets a real SQL Server rowversion column");
    }

    [Fact]
    public async Task RegenerateAsync_CalledTwice_LeavesExactlyOneRowForTheUser()
    {
        await using var fixture = await SqlServerCalendarFeedFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ICalendarFeedTokenService>();

        await tokenService.RegenerateAsync();
        await tokenService.RegenerateAsync();

        await using var readScope = fixture.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var rowCount = await db.CalendarFeedTokens.CountAsync(t => t.UserId == UserA);

        rowCount.Should().Be(1, "the unique index on UserId means regenerating must overwrite the existing row, never insert a second one");
    }

    [Fact]
    public async Task RevokeAsync_BreaksResolutionImmediatelyAgainstRealSqlServer()
    {
        await using var fixture = await SqlServerCalendarFeedFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ICalendarFeedTokenService>();
        var result = await tokenService.RegenerateAsync();

        await tokenService.RevokeAsync();

        (await tokenService.ResolveUserIdAsync(result.RawToken)).Should().BeNull();
    }

    [Fact]
    public async Task GetFeedEventsAsync_NeverReturnsAnotherUsersEventsFromRealSqlServer()
    {
        await using var fixture = await SqlServerCalendarFeedFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using (var seedScope = fixture.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            var projectA = new Project
            {
                Id = Guid.NewGuid(), UserId = UserA, Name = "A's project",
                Status = ProjectStatus.Active, Priority = ProjectPriority.Medium,
                DueDate = new DateTime(2026, 9, 1)
            };
            var projectB = new Project
            {
                Id = Guid.NewGuid(), UserId = UserB, Name = "B's project",
                Status = ProjectStatus.Active, Priority = ProjectPriority.Medium,
                DueDate = new DateTime(2026, 9, 1)
            };
            db.Projects.AddRange(projectA, projectB);
            db.Tasks.AddRange(
                new TaskItem
                {
                    Id = Guid.NewGuid(), UserId = UserA, ProjectId = projectA.Id, Title = "A's task",
                    Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, DueDate = new DateTime(2026, 9, 2)
                },
                new TaskItem
                {
                    Id = Guid.NewGuid(), UserId = UserB, ProjectId = projectB.Id, Title = "B's task",
                    Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium, DueDate = new DateTime(2026, 9, 2)
                });
            await db.SaveChangesAsync();
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var feedService = scope.ServiceProvider.GetRequiredService<ICalendarFeedService>();

        var eventsForA = await feedService.GetFeedEventsAsync(UserA);
        var eventsForB = await feedService.GetFeedEventsAsync(UserB);

        eventsForA.Should().OnlyContain(e => e.Title == "A's project" || e.Title == "A's task");
        eventsForB.Should().OnlyContain(e => e.Title == "B's project" || e.Title == "B's task");
    }

    private sealed class SqlServerCalendarFeedFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerCalendarFeedFixture(string masterConnectionString, string databaseName, ServiceProvider services)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public static async Task<SqlServerCalendarFeedFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server calendar-feed tests.");

            var databaseName = $"BrainyCalendarFeed_{Guid.NewGuid():N}";
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
                await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");
            }
            catch (Exception ex) when (!isExplicitlyConfigured && ex is SqlException or InvalidOperationException)
            {
                throw SkipException.ForSkip("SQL Server LocalDB is unavailable and BRAINY_TEST_SQL_CONNECTIONSTRING is not set.");
            }

            var services = new ServiceCollection();
            services.AddDbContext<BrainyDbContext>(options =>
                options.UseSqlServer(application.ConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(UserA));
            services.AddSingleton(TimeProvider.System);
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();
                return new SqlServerCalendarFeedFixture(master.ConnectionString, databaseName, provider);
            }
            catch
            {
                await provider.DisposeAsync();
                SqlConnection.ClearAllPools();
                await ExecuteMasterCommandAsync(
                    master.ConnectionString,
                    $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{databaseName}]");
                throw;
            }
        }

        public async Task SeedUsersAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            context.Users.AddRange(
                new ApplicationUser { Id = UserA, UserName = "calendar-feed-sql-a@example.test", NormalizedUserName = "CALENDAR-FEED-SQL-A@EXAMPLE.TEST" },
                new ApplicationUser { Id = UserB, UserName = "calendar-feed-sql-b@example.test", NormalizedUserName = "CALENDAR-FEED-SQL-B@EXAMPLE.TEST" });
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            SqlConnection.ClearAllPools();
            await ExecuteMasterCommandAsync(
                _masterConnectionString,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]");
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

    /// <summary>
    /// Always resolves to <see cref="UserA"/>: only <see cref="ICalendarFeedTokenService"/>'s
    /// current-user methods (Regenerate/Revoke/GetStatus) depend on this — the isolation test
    /// above calls <see cref="ICalendarFeedService.GetFeedEventsAsync"/> with an explicit user
    /// id for each user instead, exactly as the feed endpoint itself does.
    /// </summary>
    private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(userId);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);
    }
}
