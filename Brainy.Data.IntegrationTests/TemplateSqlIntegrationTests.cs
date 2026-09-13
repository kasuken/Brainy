using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Templates;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Brainy.Data.IntegrationTests;

/// <summary>
/// Verifies the AddTemplates migration's schema against a real SQL Server database —
/// real cascade-delete of a template's task list, real rowversion concurrency, and a
/// full instantiation through the actual FK topology — none of which EF InMemory can
/// prove (see AGENTS.md).
/// </summary>
public sealed class TemplateSqlIntegrationTests
{
    private const string UserId = "template-sql-user";

    [Fact]
    public async Task DeletingProjectTemplate_CascadeDeletesItsTaskListAtTheDatabaseLevel()
    {
        await using var fixture = await SqlServerTemplateFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

        var template = new ProjectTemplate
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = "Kickoff",
            ProjectNamePattern = "Kickoff",
            Tasks = [new ProjectTemplateTask { Id = Guid.NewGuid(), Title = "Send agenda", SortOrder = 0 }]
        };
        context.ProjectTemplates.Add(template);
        await context.SaveChangesAsync();

        context.ProjectTemplates.Remove(template);
        await context.SaveChangesAsync();

        (await context.ProjectTemplateTasks.CountAsync(t => t.ProjectTemplateId == template.Id)).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflict()
    {
        await using var fixture = await SqlServerTemplateFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IProjectTemplateService>();

        var created = await templates.CreateAsync(new CreateProjectTemplateDto("Kickoff", "Kickoff Pattern"));

        // Editor A loads the template (captures RowVersion v1); editor B saves first below.
        var staleLoad = created;
        await templates.UpdateAsync(new UpdateProjectTemplateDto(
            created.Id, "Kickoff (edited by B)", "Kickoff Pattern", RowVersion: created.RowVersion));

        var act = () => templates.UpdateAsync(new UpdateProjectTemplateDto(
            staleLoad.Id, "Kickoff (edited by A, stale)", "Kickoff Pattern", RowVersion: staleLoad.RowVersion));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task InstantiateAsync_AgainstRealDatabase_CreatesProjectAndTasksThroughRealForeignKeys()
    {
        await using var fixture = await SqlServerTemplateFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IProjectTemplateService>();
        var areas = scope.ServiceProvider.GetRequiredService<IAreaService>();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskService>();

        var area = await areas.CreateAsync(new CreateAreaDto("Client Work"));
        var template = await templates.CreateAsync(new CreateProjectTemplateDto(
            Name: "Kickoff",
            ProjectNamePattern: "Kickoff Project",
            DefaultAreaId: area.Id,
            Tasks:
            [
                new CreateProjectTemplateTaskDto("Send agenda", DueDateOffsetDays: 1),
                new CreateProjectTemplateTaskDto("Hold call", DueDateOffsetDays: 3),
            ]));

        var project = await templates.InstantiateAsync(new InstantiateProjectTemplateDto(template.Id));

        project.AreaId.Should().Be(area.Id);
        var createdTasks = await tasks.GetByProjectAsync(project.Id);
        createdTasks.Should().HaveCount(2);
        createdTasks.Should().Contain(t => t.Title == "Send agenda" && t.DueDate.HasValue);
        createdTasks.Should().Contain(t => t.Title == "Hold call" && t.DueDate.HasValue);
    }

    private sealed class SqlServerTemplateFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerTemplateFixture(string masterConnectionString, string databaseName, ServiceProvider services)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public static async Task<SqlServerTemplateFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server template tests.");

            var databaseName = $"BrainyTemplates_{Guid.NewGuid():N}";
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
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(UserId));
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();
                return new SqlServerTemplateFixture(master.ConnectionString, databaseName, provider);
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

        public async Task SeedUserAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            context.Users.Add(new ApplicationUser
            {
                Id = UserId,
                UserName = "template-sql@example.test",
                NormalizedUserName = "TEMPLATE-SQL@EXAMPLE.TEST"
            });
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

    private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(userId);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);
    }
}
