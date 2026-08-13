using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.DTOs.Tasks;
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
/// Verifies task-dependency graph serialization against a real SQL Server database.
/// </summary>
public sealed class TaskDependencyConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentOppositeDependencyMutationsCommitOnlyOneEdge(bool replaceThroughUpdate)
    {
        await using var fixture = await SqlServerTaskDependencyFixture.CreateAsync();
        var (firstTaskId, secondTaskId) = await fixture.SeedTasksAsync(replaceThroughUpdate);
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<ITaskService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ITaskService>();

        var firstFailure = Record.ExceptionAsync(() => MutateDependencyAsync(
            firstService,
            firstTaskId,
            secondTaskId,
            "First",
            replaceThroughUpdate));
        var secondFailure = Record.ExceptionAsync(() => MutateDependencyAsync(
            secondService,
            secondTaskId,
            firstTaskId,
            "Second",
            replaceThroughUpdate));

        var failures = await Task.WhenAll(firstFailure, secondFailure);

        failures.Should().ContainSingle(failure => failure == null);
        failures.OfType<InvalidOperationException>()
            .Should().ContainSingle()
            .Which.Message.Should().Contain("cycle");

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var edges = await context.TaskDependencies.AsNoTracking().ToListAsync();
        var opposingEdges = edges.Where(candidate =>
            candidate.TaskId == firstTaskId && candidate.DependsOnTaskId == secondTaskId ||
            candidate.TaskId == secondTaskId && candidate.DependsOnTaskId == firstTaskId);
        opposingEdges.Should().ContainSingle();
        edges.Should().HaveCount(replaceThroughUpdate ? 2 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDependencyAndStatusMutationPreservePrerequisiteInvariant(bool complete)
    {
        await using var fixture = await SqlServerTaskDependencyFixture.CreateAsync();
        var (taskId, prerequisiteId) = await fixture.SeedTasksAsync(withExistingDependencies: false);
        await using var dependencyScope = fixture.Services.CreateAsyncScope();
        await using var statusScope = fixture.Services.CreateAsyncScope();
        var dependencyService = dependencyScope.ServiceProvider.GetRequiredService<ITaskService>();
        var statusService = statusScope.ServiceProvider.GetRequiredService<ITaskService>();

        var dependencyFailure = Record.ExceptionAsync(
            () => dependencyService.AddDependencyAsync(taskId, prerequisiteId));
        var statusFailure = Record.ExceptionAsync(() => complete
            ? statusService.CompleteAsync(taskId)
            : statusService.SetInProgressAsync(taskId));

        var failures = await Task.WhenAll(dependencyFailure, statusFailure);

        failures.Should().ContainSingle(failure => failure == null);
        failures.OfType<InvalidOperationException>()
            .Should().ContainSingle()
            .Which.Message.Should().Contain("prerequisite");

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var task = await context.Tasks.AsNoTracking().SingleAsync(candidate => candidate.Id == taskId);
        var dependencyExists = await context.TaskDependencies.AsNoTracking()
            .AnyAsync(edge => edge.TaskId == taskId && edge.DependsOnTaskId == prerequisiteId);

        if (dependencyExists)
            task.Status.Should().Be(TaskItemStatus.Todo);
        else
            task.Status.Should().Be(complete ? TaskItemStatus.Done : TaskItemStatus.InProgress);
    }

    private static async Task MutateDependencyAsync(
        ITaskService service,
        Guid taskId,
        Guid dependsOnTaskId,
        string title,
        bool replaceThroughUpdate)
    {
        if (replaceThroughUpdate)
        {
            await service.UpdateAsync(new UpdateTaskDto(
                taskId,
                title,
                DependsOnTaskIds: [dependsOnTaskId]));
            return;
        }

        await service.AddDependencyAsync(taskId, dependsOnTaskId);
    }

    private sealed class SqlServerTaskDependencyFixture : IAsyncDisposable
    {
        private const string UserId = "task-dependency-concurrency-user";

        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerTaskDependencyFixture(
            string masterConnectionString,
            string databaseName,
            ServiceProvider services)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public static async Task<SqlServerTaskDependencyFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
            {
                throw SkipException.ForSkip(
                    "Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server task-dependency tests.");
            }

            var databaseName = $"BrainyTaskDependencies_{Guid.NewGuid():N}";
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
                await ExecuteMasterCommandAsync(
                    master.ConnectionString,
                    $"CREATE DATABASE [{databaseName}]");
            }
            catch (Exception ex) when (!isExplicitlyConfigured &&
                                       ex is SqlException or InvalidOperationException)
            {
                throw SkipException.ForSkip(
                    "SQL Server LocalDB is unavailable and BRAINY_TEST_SQL_CONNECTIONSTRING is not set.");
            }

            var services = new ServiceCollection();
            services.AddDbContext<BrainyDbContext>(options =>
                options.UseSqlServer(
                    application.ConnectionString,
                    sqlServer => sqlServer.EnableRetryOnFailure()));
            services.AddScoped<IApplicationDbContext>(
                provider => provider.GetRequiredService<BrainyDbContext>());
            services.AddSingleton<ICurrentUserService>(
                new CoordinatedCurrentUserService(UserId, participantCount: 2));
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();
                return new SqlServerTaskDependencyFixture(
                    master.ConnectionString,
                    databaseName,
                    provider);
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

        public async Task<(Guid FirstTaskId, Guid SecondTaskId)> SeedTasksAsync(
            bool withExistingDependencies)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            context.Users.Add(new ApplicationUser
            {
                Id = UserId,
                UserName = "task-dependency-concurrency@example.test",
                NormalizedUserName = "TASK-DEPENDENCY-CONCURRENCY@EXAMPLE.TEST"
            });
            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Name = "Dependency serialization"
            };
            var first = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = project,
                Title = "First"
            };
            var second = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Project = project,
                Title = "Second"
            };
            context.Tasks.AddRange(first, second);

            if (withExistingDependencies)
            {
                var firstPrerequisite = new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Project = project,
                    Title = "First prerequisite"
                };
                var secondPrerequisite = new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Project = project,
                    Title = "Second prerequisite"
                };
                context.Tasks.AddRange(firstPrerequisite, secondPrerequisite);
                context.TaskDependencies.AddRange(
                    new TaskDependency
                    {
                        Id = Guid.NewGuid(),
                        Task = first,
                        DependsOnTask = firstPrerequisite
                    },
                    new TaskDependency
                    {
                        Id = Guid.NewGuid(),
                        Task = second,
                        DependsOnTask = secondPrerequisite
                    });
            }

            await context.SaveChangesAsync();
            return (first.Id, second.Id);
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

        private static async Task ExecuteMasterCommandAsync(
            string connectionString,
            string commandText)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class CoordinatedCurrentUserService(
        string userId,
        int participantCount) : ICurrentUserService
    {
        private readonly TaskCompletionSource<bool> _participantsReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivedParticipants;

        Task<string?> ICurrentUserService.GetUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(userId);

        async Task<string> ICurrentUserService.GetRequiredUserIdAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivedParticipants) == participantCount)
                _participantsReady.TrySetResult(true);

            await _participantsReady.Task.WaitAsync(cancellationToken);
            return userId;
        }
    }
}
