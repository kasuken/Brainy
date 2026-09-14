using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.DTOs.Push;
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
/// Verifies the web push schema (issue #315) against a real SQL Server database: the unique
/// index that keeps subscriptions keyed by a hashed endpoint, the unique index enforcing one
/// preference row per user, real <c>rowversion</c> concurrency columns, and — end to end —
/// that <see cref="IPushDispatchService"/>'s LINQ translates and runs correctly against SQL
/// Server rather than only EF InMemory (see AGENTS.md).
/// </summary>
public sealed class PushNotificationSqlIntegrationTests
{
    private const string UserA = "push-sql-user-a";
    private const string UserB = "push-sql-user-b";

    [Fact]
    public async Task PushSubscription_EndpointHash_UniqueIndex_RejectsASecondRowForTheSameEndpoint()
    {
        await using var fixture = await SqlServerPushFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

        const string sameHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        db.PushSubscriptions.Add(new PushSubscription
        {
            Id = Guid.NewGuid(), UserId = UserA, Endpoint = "https://push.example.com/1",
            EndpointHash = sameHash, P256dh = "k", Auth = "a"
        });
        await db.SaveChangesAsync();

        db.PushSubscriptions.Add(new PushSubscription
        {
            Id = Guid.NewGuid(), UserId = UserB, Endpoint = "https://push.example.com/2",
            EndpointHash = sameHash, P256dh = "k", Auth = "a"
        });

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "EndpointHash has a unique index, so two rows can never share it even across users");
    }

    [Fact]
    public async Task PushSubscription_HasARealSqlServerRowVersionColumn()
    {
        await using var fixture = await SqlServerPushFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

        var subscription = new PushSubscription
        {
            Id = Guid.NewGuid(), UserId = UserA, Endpoint = "https://push.example.com/1",
            EndpointHash = "hash-1", P256dh = "k", Auth = "a"
        };
        db.PushSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        subscription.RowVersion.Should().NotBeNull(
            "PushSubscription derives from BaseEntity, which gets a real rowversion column");
    }

    [Fact]
    public async Task PushNotificationPreference_UserId_UniqueIndex_RejectsASecondRowForTheSameUser()
    {
        await using var fixture = await SqlServerPushFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

        db.PushNotificationPreferences.Add(new PushNotificationPreference { Id = Guid.NewGuid(), UserId = UserA, Enabled = true });
        await db.SaveChangesAsync();

        db.PushNotificationPreferences.Add(new PushNotificationPreference { Id = Guid.NewGuid(), UserId = UserA, Enabled = false });

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("exactly one preference row may exist per user");
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_RunsEndToEndAgainstRealSqlServer()
    {
        await using var fixture = await SqlServerPushFixture.CreateAsync();
        await fixture.SeedUsersAsync();

        await using (var seedScope = fixture.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            var project = new Project
            {
                Id = Guid.NewGuid(), UserId = UserA, Name = "P",
                Status = ProjectStatus.Active, Priority = ProjectPriority.Medium
            };
            db.Projects.Add(project);
            db.Tasks.Add(new TaskItem
            {
                Id = Guid.NewGuid(), UserId = UserA, ProjectId = project.Id, Title = "Overdue task",
                Status = TaskItemStatus.Todo, Priority = TaskPriority.Medium,
                DueDate = fixture.Now.UtcDateTime.Date.AddDays(-1)
            });
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(), UserId = UserA, Endpoint = "https://push.example.com/a",
                EndpointHash = "hash-a", P256dh = "k", Auth = "a"
            });
            db.PushNotificationPreferences.Add(new PushNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = UserA,
                Enabled = true,
                DailyFocusNudgeEnabled = false,
                WeeklyReviewReminderEnabled = false,
            });
            await db.SaveChangesAsync();
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatchService = scope.ServiceProvider.GetRequiredService<IPushDispatchService>();

        var result = await dispatchService.DispatchDueNotificationsAsync();

        result.UsersEvaluated.Should().Be(1);
        result.NotificationsSent.Should().Be(1);

        await using var readScope = fixture.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var log = await readDb.PushNotificationDeliveryLogs.AsNoTracking().SingleAsync();
        log.UserId.Should().Be(UserA);
        log.Category.Should().Be(PushNotificationCategory.OverdueTask);
    }

    private sealed class SqlServerPushFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerPushFixture(string masterConnectionString, string databaseName, ServiceProvider services, DateTimeOffset now)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
            Now = now;
        }

        public ServiceProvider Services { get; }

        public DateTimeOffset Now { get; }

        public static async Task<SqlServerPushFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server push-notification tests.");

            var databaseName = $"BrainyPush_{Guid.NewGuid():N}";
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

            // Sunday, so the daily/weekly time gates in DispatchDueNotificationsAsync are
            // irrelevant to this end-to-end test (only the overdue-task category is enabled).
            var now = new DateTimeOffset(2026, 6, 14, 18, 0, 0, TimeSpan.Zero);

            var services = new ServiceCollection();
            services.AddDbContext<BrainyDbContext>(options =>
                options.UseSqlServer(application.ConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            services.AddScoped<FakeBackgroundUserContextAccessor>();
            services.AddScoped<IBackgroundUserContextAccessor>(sp => sp.GetRequiredService<FakeBackgroundUserContextAccessor>());
            services.AddScoped<ICurrentUserService, AmbientCurrentUserService>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            services.AddSingleton<IPushNotificationSender, AlwaysSucceedsPushNotificationSender>();
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();
                return new SqlServerPushFixture(master.ConnectionString, databaseName, provider, now);
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
                new ApplicationUser { Id = UserA, UserName = "push-sql-a@example.test", NormalizedUserName = "PUSH-SQL-A@EXAMPLE.TEST" },
                new ApplicationUser { Id = UserB, UserName = "push-sql-b@example.test", NormalizedUserName = "PUSH-SQL-B@EXAMPLE.TEST" });
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

    /// <summary>Scoped impersonation holder — mirrors <c>Brainy.Web.Identity.BackgroundUserContext</c>.</summary>
    private sealed class FakeBackgroundUserContextAccessor : IBackgroundUserContextAccessor
    {
        public string? UserId { get; set; }
    }

    /// <summary>Mirrors <c>Brainy.Web.Identity.CurrentUserService</c>'s impersonation check.</summary>
    private sealed class AmbientCurrentUserService(IBackgroundUserContextAccessor backgroundUserContext) : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(backgroundUserContext.UserId);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(backgroundUserContext.UserId ?? throw new UnauthorizedAccessException("No impersonated user set."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AlwaysSucceedsPushNotificationSender : IPushNotificationSender
    {
        public Task<PushSendResult> SendAsync(
            PushSubscriptionEndpointDto subscription,
            PushNotificationPayload payload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PushSendResult.Sent());
    }
}
