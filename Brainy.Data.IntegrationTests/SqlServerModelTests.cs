using Brainy.Data.Identity;
using Brainy.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Brainy.Data.IntegrationTests;

public sealed class SqlServerModelTests
{
    [Fact]
    public async Task MigrationsEnforceUniquenessAndRowVersion()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
        if (string.IsNullOrWhiteSpace(configuredConnection))
            return;

        var databaseName = $"BrainyIntegration_{Guid.NewGuid():N}";
        var master = new SqlConnectionStringBuilder(configuredConnection)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true
        };
        var application = new SqlConnectionStringBuilder(master.ConnectionString)
        {
            InitialCatalog = databaseName
        };

        await WaitForSqlAsync(master.ConnectionString);
        await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");

        try
        {
            var options = new DbContextOptionsBuilder<BrainyDbContext>()
                .UseSqlServer(application.ConnectionString)
                .Options;

            await using var database = new BrainyDbContext(options);
            await database.Database.MigrateAsync();

            var pending = await database.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "integration@example.test",
                NormalizedUserName = "INTEGRATION@EXAMPLE.TEST",
                Email = "integration@example.test",
                NormalizedEmail = "INTEGRATION@EXAMPLE.TEST"
            };
            var area = new Area { Id = Guid.NewGuid(), UserId = user.Id, Name = "Integration" };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AreaId = area.Id,
                Name = "Migration validation"
            };
            var first = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProjectId = project.Id,
                Title = "First",
                IsCurrentTask = true
            };
            var second = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProjectId = project.Id,
                Title = "Second"
            };

            database.Users.Add(user);
            database.Areas.Add(area);
            database.Projects.Add(project);
            database.Tasks.AddRange(first, second);
            await database.SaveChangesAsync();

            Assert.NotNull(first.RowVersion);
            var originalRowVersion = first.RowVersion!.ToArray();
            first.Title = "First updated";
            await database.SaveChangesAsync();
            Assert.NotEmpty(first.RowVersion);
            Assert.False(originalRowVersion.SequenceEqual(first.RowVersion));

            second.IsCurrentTask = true;
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());

            database.ChangeTracker.Clear();
            database.DashboardPreferences.AddRange(
                new UserDashboardPreference { Id = Guid.NewGuid(), UserId = user.Id },
                new UserDashboardPreference { Id = Guid.NewGuid(), UserId = user.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        }
        finally
        {
            await ExecuteMasterCommandAsync(
                master.ConnectionString,
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
        }
    }

    private static async Task WaitForSqlAsync(string connectionString)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 30; attempt++)
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
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException("SQL Server did not become ready for integration tests.", lastFailure);
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
