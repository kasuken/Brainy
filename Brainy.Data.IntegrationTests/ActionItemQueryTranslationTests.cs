using AwesomeAssertions;
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

/// <summary>
/// Guards against EF Core LINQ-translation regressions in <see cref="IActionItemService"/>.
/// A prior implementation filtered/ordered an already-projected <c>IQueryable&lt;ActionItemDto&gt;</c>,
/// which EF Core cannot translate to SQL against a real provider (it works against a client-evaluated
/// InMemory provider, which is why this must run against SQL Server rather than EF InMemory).
/// </summary>
public sealed class ActionItemQueryTranslationTests
{
    private const string UserId = "action-item-query-translation-user";

    [Fact]
    public async Task GetByNoteAsyncTranslatesAndOrdersWithoutClientEvaluation()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
        var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
        if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
        {
            configuredConnection =
                "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
        }

        if (string.IsNullOrWhiteSpace(configuredConnection))
            throw SkipException.ForSkip(
                "Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server action-item query tests.");

        var databaseName = $"BrainyActionItemQuery_{Guid.NewGuid():N}";
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
            throw SkipException.ForSkip(
                "SQL Server LocalDB is unavailable and BRAINY_TEST_SQL_CONNECTIONSTRING is not set.");
        }

        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(UserId));
        services.AddBrainyApplication();
        services.AddDisabledAiAssistant();

        var provider = services.BuildServiceProvider();
        try
        {
            Guid noteId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();

                context.Users.Add(new ApplicationUser
                {
                    Id = UserId,
                    UserName = "action-item-query@example.test",
                    NormalizedUserName = "ACTION-ITEM-QUERY@EXAMPLE.TEST"
                });
                var note = new Note { Id = Guid.NewGuid(), UserId = UserId, Title = "Note", Content = "Content" };
                var project = new Project { Id = Guid.NewGuid(), UserId = UserId, Name = "Project" };
                var task = new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    Project = project,
                    Title = "Promoted task"
                };
                context.Notes.Add(note);
                context.Projects.Add(project);
                context.Tasks.Add(task);
                context.ActionItems.AddRange(
                    new ActionItem
                    {
                        Id = Guid.NewGuid(),
                        UserId = UserId,
                        Note = note,
                        Title = "Done action",
                        Status = ActionItemStatus.Done
                    },
                    new ActionItem
                    {
                        Id = Guid.NewGuid(),
                        UserId = UserId,
                        Note = note,
                        Title = "Open action promoted to task",
                        Status = ActionItemStatus.Open,
                        TaskItem = task
                    },
                    new ActionItem
                    {
                        Id = Guid.NewGuid(),
                        UserId = UserId,
                        Note = note,
                        Title = "Dismissed action",
                        Status = ActionItemStatus.Dismissed
                    });
                await context.SaveChangesAsync();
                noteId = note.Id;
            }

            await using var verificationScope = provider.CreateAsyncScope();
            var service = verificationScope.ServiceProvider.GetRequiredService<IActionItemService>();

            // Must not throw System.InvalidOperationException ("could not be translated") — the
            // regression this test guards against.
            var actions = await service.GetByNoteAsync(noteId);

            actions.Should().HaveCount(3);
            // Ordering: not-Done before Done, and within not-Done, not-Dismissed before Dismissed.
            actions.Select(a => a.Title).Should().ContainInOrder(
                "Open action promoted to task", "Dismissed action", "Done action");
            actions.Single(a => a.Title == "Open action promoted to task").ProjectId.Should().NotBeNull();
        }
        finally
        {
            await provider.DisposeAsync();
            SqlConnection.ClearAllPools();
            await ExecuteMasterCommandAsync(
                master.ConnectionString,
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
        }
    }

    private static async Task ExecuteMasterCommandAsync(string connectionString, string commandText)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
    {
        Task<string?> ICurrentUserService.GetUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(userId);

        Task<string> ICurrentUserService.GetRequiredUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(userId);
    }
}
