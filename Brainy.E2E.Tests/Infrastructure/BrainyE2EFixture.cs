using Brainy.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>
/// Shared, once-per-run scaffolding for the whole E2E suite: one throwaway SQL Server
/// database (created fresh and dropped at the end — see class remarks), one real
/// Kestrel-hosted Brainy.Web instance, and one shared Chromium instance.
///
/// Design note on isolation: rather than a fresh database (slow: a full app boot + migration)
/// or a fresh app instance per test, every test gets its own brand-new registered user (see
/// <see cref="E2ETestBase.RegisterNewUserAsync"/>). Every read/write in Brainy is already
/// scoped to the authenticated user (see AGENTS.md), so distinct users never observe each
/// other's data even though they share one database and one running app — this is what makes
/// per-test seeding deterministic without needing a per-test database.
/// </summary>
public sealed class BrainyE2EFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvVar = "BRAINY_TEST_SQL_CONNECTIONSTRING";

    private string? _databaseName;
    private SqlConnectionStringBuilder? _masterConnection;
    private BrainyAppFactory? _factory;

    public string BaseUrl { get; private set; } = string.Empty;

    public IPlaywright Playwright { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvVar} is not set. The E2E suite needs a real SQL Server " +
                "instance to run against (see docs/roadmap/release-7/15-e2e-tests.md) — it does " +
                "not run against EF InMemory.");
        }

        _masterConnection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true
        };
        _databaseName = $"BrainyE2E_{Guid.NewGuid():N}";
        var appConnection = new SqlConnectionStringBuilder(_masterConnection.ConnectionString)
        {
            InitialCatalog = _databaseName
        };

        await WaitForSqlAsync(_masterConnection.ConnectionString);
        await ExecuteMasterCommandAsync(_masterConnection.ConnectionString, $"CREATE DATABASE [{_databaseName}]");

        var options = new DbContextOptionsBuilder<BrainyDbContext>()
            .UseSqlServer(appConnection.ConnectionString)
            .Options;
        await using (var db = new BrainyDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _factory = new BrainyAppFactory(appConnection.ConnectionString);
        // Forces WebApplicationFactory to actually start Kestrel and bind a real port; the
        // resulting address is then readable from ClientOptions.BaseAddress.
        _ = _factory.Services;
        BaseUrl = _factory.ClientOptions.BaseAddress!.ToString().TrimEnd('/');

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        var launchOptions = new BrowserTypeLaunchOptions { Headless = true };
        // This sandbox pre-installs Chromium outside Playwright's normal managed cache (see
        // docs/roadmap/release-7/15-e2e-tests.md's environment notes) — point at it directly
        // when present instead of letting Playwright resolve/download a browser. CI installs
        // browsers the normal way (see .github/workflows/ci.yml), where this path won't exist
        // and default resolution is used instead.
        const string preInstalledChromium = "/opt/pw-browsers/chromium";
        if (File.Exists(preInstalledChromium))
        {
            launchOptions.ExecutablePath = preInstalledChromium;
        }

        Browser = await Playwright.Chromium.LaunchAsync(launchOptions);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_masterConnection is not null && _databaseName is not null)
        {
            await ExecuteMasterCommandAsync(
                _masterConnection.ConnectionString,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
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

        throw new InvalidOperationException("SQL Server did not become ready for the E2E suite.", lastFailure);
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

/// <summary>xUnit collection wiring: every test class in the suite shares one <see cref="BrainyE2EFixture"/>
/// (one app boot, one database, one browser) and runs without cross-class parallelism, since they
/// all share that one running app instance.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<BrainyE2EFixture>
{
    public const string Name = "Brainy E2E";
}
