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
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Brainy.Data.IntegrationTests;

/// <summary>
/// Verifies the output share-link schema against a real SQL Server database — the unique
/// index that keeps exactly one link row per output, the real <c>rowversion</c> concurrency
/// column, and the migration that created the table — none of which EF InMemory can prove
/// (see AGENTS.md). Also proves, against real persisted data, that revocation and expiry take
/// effect immediately. Cross-user ownership boundaries (which never touch anything SQL-Server-
/// specific) are covered in <c>Brainy.Application.Tests.Services.OutputShareLinkServiceTests</c>
/// instead, exactly as the calendar-feed token's unit and SQL integration tests split that
/// same coverage.
/// </summary>
public sealed class OutputShareLinkSqlIntegrationTests
{
    private const string UserA = "output-share-sql-user-a";

    [Fact]
    public async Task EnableAsync_PersistsOnlyAHash_NeverTheRawToken()
    {
        await using var fixture = await SqlServerOutputShareFixture.CreateAsync();
        await fixture.SeedUserAndOutputAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IOutputShareLinkService>();

        var result = await shareService.EnableAsync(fixture.OutputId, expiresAtUtc: null);

        await using var readScope = fixture.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var row = await db.OutputShareLinks.AsNoTracking().SingleAsync(l => l.OutputId == fixture.OutputId);

        row.TokenHash.Should().NotBe(result.RawToken);
        row.TokenHash.Should().HaveLength(64);
        row.RowVersion.Should().NotBeNull("OutputShareLink derives from BaseEntity, which gets a real SQL Server rowversion column");
    }

    [Fact]
    public async Task EnableAsync_CalledTwice_LeavesExactlyOneRowForTheOutput()
    {
        await using var fixture = await SqlServerOutputShareFixture.CreateAsync();
        await fixture.SeedUserAndOutputAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IOutputShareLinkService>();

        await shareService.EnableAsync(fixture.OutputId, expiresAtUtc: null);
        await shareService.EnableAsync(fixture.OutputId, expiresAtUtc: null);

        await using var readScope = fixture.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var rowCount = await db.OutputShareLinks.CountAsync(l => l.OutputId == fixture.OutputId);

        rowCount.Should().Be(1, "the unique index on OutputId means enabling twice must overwrite the existing row, never insert a second one");
    }

    [Fact]
    public async Task RevokeAsync_BreaksResolutionImmediatelyAgainstRealSqlServer()
    {
        await using var fixture = await SqlServerOutputShareFixture.CreateAsync();
        await fixture.SeedUserAndOutputAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IOutputShareLinkService>();
        var result = await shareService.EnableAsync(fixture.OutputId, expiresAtUtc: null);

        await shareService.RevokeAsync(fixture.OutputId);

        (await shareService.ResolvePublicAsync(result.RawToken)).Should().BeNull();
    }

    [Fact]
    public async Task ResolvePublicAsync_AfterExpiry_ReturnsNullAgainstRealSqlServer()
    {
        await using var fixture = await SqlServerOutputShareFixture.CreateAsync();
        await fixture.SeedUserAndOutputAsync();
        // EnableAsync rejects a past expiry outright (see its own validation, covered in
        // Application.Tests), so an already-expired row is seeded directly here — exactly
        // what would exist a moment after a link's future expiry passes for real.
        var rawToken = await fixture.SeedExpiredShareLinkAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IOutputShareLinkService>();

        (await shareService.ResolvePublicAsync(rawToken)).Should().BeNull("an already-past expiry must resolve to nothing, exactly like a revoked link");
    }

    [Fact]
    public async Task ResolvePublicAsync_WithAValidToken_ReturnsOnlyTheOutputsOwnContentAgainstRealSqlServer()
    {
        await using var fixture = await SqlServerOutputShareFixture.CreateAsync();
        await fixture.SeedUserAndOutputAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IOutputShareLinkService>();
        var result = await shareService.EnableAsync(fixture.OutputId, expiresAtUtc: null);

        var shared = await shareService.ResolvePublicAsync(result.RawToken);

        shared.Should().NotBeNull();
        shared!.Title.Should().Be(SqlServerOutputShareFixture.OutputTitle);
        shared.Content.Should().Be(SqlServerOutputShareFixture.OutputContent);
    }

    private sealed class SqlServerOutputShareFixture : IAsyncDisposable
    {
        public const string OutputTitle = "Sql Output";
        public const string OutputContent = "Body content.";

        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerOutputShareFixture(string masterConnectionString, string databaseName, ServiceProvider services)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public Guid OutputId { get; private set; }

        public static async Task<SqlServerOutputShareFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server output-share tests.");

            var databaseName = $"BrainyOutputShare_{Guid.NewGuid():N}";
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
                return new SqlServerOutputShareFixture(master.ConnectionString, databaseName, provider);
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

        public async Task SeedUserAndOutputAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            context.Users.Add(new ApplicationUser
            {
                Id = UserA,
                UserName = "output-share-sql-a@example.test",
                NormalizedUserName = "OUTPUT-SHARE-SQL-A@EXAMPLE.TEST"
            });
            var output = new Output
            {
                Id = Guid.NewGuid(),
                UserId = UserA,
                Title = OutputTitle,
                Content = OutputContent,
                Type = OutputType.Report,
                Status = OutputStatus.Draft
            };
            context.Outputs.Add(output);
            await context.SaveChangesAsync();
            OutputId = output.Id;
        }

        /// <summary>
        /// Seeds a share link row whose expiry is already in the past, bypassing
        /// <see cref="IOutputShareLinkService.EnableAsync"/>'s own future-only validation, and
        /// returns the raw token that hashes to it.
        /// </summary>
        public async Task<string> SeedExpiredShareLinkAsync()
        {
            var rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            context.OutputShareLinks.Add(new OutputShareLink
            {
                Id = Guid.NewGuid(),
                UserId = UserA,
                OutputId = OutputId,
                TokenHash = tokenHash,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1)
            });
            await context.SaveChangesAsync();

            return rawToken;
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

    /// <summary>Always resolves to <see cref="UserA"/> — the only owner in this fixture.</summary>
    private sealed class FixedCurrentUserService(string userId) : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(userId);

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);
    }
}
