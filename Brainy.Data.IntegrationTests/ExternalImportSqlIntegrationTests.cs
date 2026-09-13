using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data;
using Brainy.Data.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Data.IntegrationTests;

/// <summary>
/// Proves issue #312's importer against a real SQL Server database (not EF InMemory):
/// migrations apply, the real unique indexes/foreign keys are in play, notes/tags/
/// relationships persist correctly end to end, and the import is properly scoped per
/// user. EF InMemory does not enforce these constraints or use real transactions
/// (<c>BrainyDbContext.ExecuteInTransactionAsync</c> bypasses the transaction entirely
/// when <c>Database.IsRelational()</c> is false), so the Application-layer unit tests in
/// <c>ExternalImportServiceObsidianTests</c> cannot stand in for this.
///
/// A dedicated "throws after some rows are already staged" rollback test is not included
/// here: <c>ExternalImportService.RunPlanAsync</c> resolves every duplicate (notes by
/// content key, tags by name, relationships by (source, target, type)) against the
/// database before ever staging a write, so a legitimately parsed source cannot reach
/// <c>SaveChangesAsync</c> with a row that would violate a unique index — there is no
/// non-contrived way to manufacture that failure from parsed content. The
/// all-or-nothing guarantee (nothing is written when anything fails) is proven instead
/// the same way <c>DataImportServiceTests</c> proves it for the JSON importer: a rejected
/// input throws before anything is staged, and the round trip below proves the success
/// path actually commits against a real database.
/// </summary>
public sealed class ExternalImportSqlIntegrationTests
{
    private const string UserId = "sql-import-user";
    private const string OtherUserId = "sql-import-other-user";

    [Fact]
    public async Task ImportCurrentUserAsync_ObsidianVault_PersistsNotesTagsAndRelationshipsAgainstRealSqlServer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var zip = BuildVaultZip(new Dictionary<string, string>
        {
            ["Notes/Alpha.md"] = "---\ntags: [\"work\", \"idea\"]\n---\n\n# Alpha\n\nRelated to [[Beta]].",
            ["Notes/Beta.md"] = "# Beta\n\nUnrelated body."
        });

        var result = await fixture.Sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, zip);

        result.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(2);
        result.EntityOutcomes.Single(o => o.EntityType == "Tags").Created.Should().Be(2);
        result.EntityOutcomes.Single(o => o.EntityType == "Note relationships").Created.Should().Be(1);

        var alpha = await fixture.Db.Notes.Include(n => n.Tags).SingleAsync(n => n.Title == "Alpha");
        var beta = await fixture.Db.Notes.SingleAsync(n => n.Title == "Beta");
        alpha.Tags.Select(t => t.Name).Should().BeEquivalentTo(["work", "idea"]);

        var relationship = await fixture.Db.NoteRelationships.SingleAsync();
        relationship.SourceNoteId.Should().Be(alpha.Id);
        relationship.TargetNoteId.Should().Be(beta.Id);

        // Re-importing the same vault against the real database is still a no-op.
        var replay = await fixture.Sut.ImportCurrentUserAsync(
            ExternalImportSourceFormat.ObsidianVault,
            BuildVaultZip(new Dictionary<string, string>
            {
                ["Notes/Alpha.md"] = "---\ntags: [\"work\", \"idea\"]\n---\n\n# Alpha\n\nRelated to [[Beta]].",
                ["Notes/Beta.md"] = "# Beta\n\nUnrelated body."
            }));

        replay.EntityOutcomes.Single(o => o.EntityType == "Notes").Created.Should().Be(0);
        (await fixture.Db.Notes.CountAsync()).Should().Be(2);
        (await fixture.Db.Tags.CountAsync()).Should().Be(2);
        (await fixture.Db.NoteRelationships.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_IsScopedToTheImportingUserOnRealSqlServer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entries = new Dictionary<string, string> { ["Notes/Shared title.md"] = "# Shared title\n\nSame body." };

        await fixture.Sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, BuildVaultZip(entries));
        await fixture.OtherUserSut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, BuildVaultZip(entries));

        (await fixture.Db.Notes.CountAsync(n => n.UserId == UserId)).Should().Be(1);
        (await fixture.Db.Notes.CountAsync(n => n.UserId == OtherUserId)).Should().Be(1);
        (await fixture.Db.Notes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ImportCurrentUserAsync_WithMalformedArchive_ThrowsAndWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var garbage = new MemoryStream(Encoding.UTF8.GetBytes("not a zip file"));

        var act = () => fixture.Sut.ImportCurrentUserAsync(ExternalImportSourceFormat.ObsidianVault, garbage);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Db.Notes.CountAsync()).Should().Be(0);
        (await fixture.Db.Tags.CountAsync()).Should().Be(0);
    }

    private static MemoryStream BuildVaultZip(IReadOnlyDictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return new MemoryStream(stream.ToArray());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly AsyncServiceScope _otherUserScope;

        private Fixture(
            string masterConnectionString, string databaseName, ServiceProvider provider,
            AsyncServiceScope scope, AsyncServiceScope otherUserScope)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _provider = provider;
            _scope = scope;
            _otherUserScope = otherUserScope;
        }

        public BrainyDbContext Db => _scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        public IExternalImportService Sut => _scope.ServiceProvider.GetRequiredService<IExternalImportService>();
        public IExternalImportService OtherUserSut => _otherUserScope.ServiceProvider.GetRequiredService<IExternalImportService>();

        public static async Task<Fixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw Xunit.Sdk.SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server importer tests.");

            var databaseName = $"BrainyExternalImport_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configuredConnection)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true
            };
            var application = new SqlConnectionStringBuilder(master.ConnectionString)
            {
                InitialCatalog = databaseName
            };

            await ExecuteMasterCommandAsync(master.ConnectionString, $"CREATE DATABASE [{databaseName}]");

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(application.ConnectionString));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            var migrationScope = provider.CreateAsyncScope();
            var context = migrationScope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await context.Database.MigrateAsync();

            // Note has a real FK to AspNetUsers on SQL Server (unlike EF InMemory).
            context.Users.AddRange(
                new ApplicationUser
                {
                    Id = UserId, UserName = "sql-import-user@example.test", NormalizedUserName = "SQL-IMPORT-USER@EXAMPLE.TEST",
                    Email = "sql-import-user@example.test", NormalizedEmail = "SQL-IMPORT-USER@EXAMPLE.TEST"
                },
                new ApplicationUser
                {
                    Id = OtherUserId, UserName = "sql-import-other-user@example.test", NormalizedUserName = "SQL-IMPORT-OTHER-USER@EXAMPLE.TEST",
                    Email = "sql-import-other-user@example.test", NormalizedEmail = "SQL-IMPORT-OTHER-USER@EXAMPLE.TEST"
                });
            await context.SaveChangesAsync();
            await migrationScope.DisposeAsync();

            var scope = BuildScopedProvider(application.ConnectionString, UserId);
            var otherUserScope = BuildScopedProvider(application.ConnectionString, OtherUserId);

            return new Fixture(master.ConnectionString, databaseName, provider, scope, otherUserScope);
        }

        private static AsyncServiceScope BuildScopedProvider(string connectionString, string userId)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(userId));
            services.AddDbContext<BrainyDbContext>(options => options.UseSqlServer(connectionString));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
            services.AddBrainyApplication();

            return services.BuildServiceProvider().CreateAsyncScope();
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _otherUserScope.DisposeAsync();
            await _provider.DisposeAsync();
            await ExecuteMasterCommandAsync(
                _masterConnectionString,
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
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
            public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(userId);

            public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(userId);
        }
    }
}
