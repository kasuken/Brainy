using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Notes;
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
/// Verifies note revision history against a real SQL Server database: that a stale
/// rowversion update still surfaces as a conflict rather than silently forking revision
/// history, and that retention (a configured <c>NoteRevision</c> retention rule, plus the
/// unconditional hard cap) actually purges rows via a real migration and FK topology —
/// neither of which EF InMemory can prove (see AGENTS.md).
/// </summary>
public sealed class NoteRevisionSqlIntegrationTests
{
    private const string UserId = "note-revision-sql-user";

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConflictAndDoesNotForkRevisionHistory()
    {
        await using var fixture = await SqlServerNoteRevisionFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var noteService = scope.ServiceProvider.GetRequiredService<INoteService>();
        var revisionService = scope.ServiceProvider.GetRequiredService<INoteRevisionService>();

        var created = await noteService.CreateAsync(new CreateNoteDto("Title", "Original content"));

        // Editor A loads the note (captures RowVersion v1) and keeps it open while editor B
        // (below) saves first.
        var staleLoad = created;

        var savedByEditorB = await noteService.UpdateAsync(new UpdateNoteDto(
            created.Id, "Title", "Editor B's content", null, created.Status, created.ParaCategory,
            null, null, null, RowVersion: created.RowVersion));

        // Editor A now saves against the RowVersion it loaded before editor B's save landed.
        var act = () => noteService.UpdateAsync(new UpdateNoteDto(
            staleLoad.Id, "Title", "Editor A's stale content", null, staleLoad.Status, staleLoad.ParaCategory,
            null, null, null, RowVersion: staleLoad.RowVersion));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        var timeline = await revisionService.GetTimelineAsync(created.Id);
        timeline.Should().HaveCount(2, "the rejected stale update must not append a third, forked revision");
        timeline[0].Content.Should().Be("Editor B's content");
        timeline[1].Content.Should().Be("Original content");

        var persisted = await noteService.GetByIdAsync(created.Id);
        persisted!.Content.Should().Be(savedByEditorB.Content);
    }

    [Fact]
    public async Task RestoreAsync_WithStaleRowVersion_ThrowsConflictAndDoesNotForkRevisionHistory()
    {
        await using var fixture = await SqlServerNoteRevisionFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var noteService = scope.ServiceProvider.GetRequiredService<INoteService>();
        var revisionService = scope.ServiceProvider.GetRequiredService<INoteRevisionService>();

        var created = await noteService.CreateAsync(new CreateNoteDto("Title", "Original content"));
        var afterFirstEdit = await noteService.UpdateAsync(new UpdateNoteDto(
            created.Id, "Title", "Second content", null, created.Status, created.ParaCategory,
            null, null, null, RowVersion: created.RowVersion));

        var firstRevisionId = (await revisionService.GetTimelineAsync(created.Id))
            .Single(r => r.Content == "Original content").Id;

        // Someone else edits the note again after afterFirstEdit's RowVersion was captured.
        await noteService.UpdateAsync(new UpdateNoteDto(
            created.Id, "Title", "Third content", null, afterFirstEdit.Status, afterFirstEdit.ParaCategory,
            null, null, null, RowVersion: afterFirstEdit.RowVersion));

        // A restore built against the now-stale RowVersion must be rejected, not silently applied.
        var act = () => revisionService.RestoreAsync(created.Id, firstRevisionId, afterFirstEdit.RowVersion);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        var timeline = await revisionService.GetTimelineAsync(created.Id);
        timeline.Should().HaveCount(3, "the rejected stale restore must not append a fourth revision");
        timeline[0].Content.Should().Be("Third content");

        var persisted = await noteService.GetByIdAsync(created.Id);
        persisted!.Content.Should().Be("Third content");
    }

    [Fact]
    public async Task RetentionRule_PurgesOlderRevisions_ButAlwaysKeepsTheNewestOne()
    {
        await using var fixture = await SqlServerNoteRevisionFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var noteService = scope.ServiceProvider.GetRequiredService<INoteService>();
        var revisionService = scope.ServiceProvider.GetRequiredService<INoteRevisionService>();
        var retentionService = scope.ServiceProvider.GetRequiredService<IArchiveRetentionService>();
        var time = fixture.Time;

        time.UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = await noteService.CreateAsync(new CreateNoteDto("Title", "v0 (40 days old)"));

        time.UtcNow = time.UtcNow.AddDays(20);
        var afterSecond = await noteService.UpdateAsync(new UpdateNoteDto(
            created.Id, "Title", "v1 (20 days old)", null, created.Status, created.ParaCategory,
            null, null, null, RowVersion: created.RowVersion));

        // Retain revisions for only 10 days; both revisions above are already older than that
        // by the time the next edit lands, so only the newest edit below should survive.
        await retentionService.UpsertRuleAsync("NoteRevision", retentionDays: 10);

        time.UtcNow = time.UtcNow.AddDays(20);
        await noteService.UpdateAsync(new UpdateNoteDto(
            created.Id, "Title", "v2 (current)", null, afterSecond.Status, afterSecond.ParaCategory,
            null, null, null, RowVersion: afterSecond.RowVersion));

        var timeline = await revisionService.GetTimelineAsync(created.Id);

        timeline.Should().ContainSingle("retention purges everything older than the cutoff except the newest revision");
        timeline[0].Content.Should().Be("v2 (current)");
    }

    [Fact]
    public async Task HardCap_TrimsRevisionsEvenWithoutAConfiguredRetentionRule()
    {
        await using var fixture = await SqlServerNoteRevisionFixture.CreateAsync();
        await fixture.SeedUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var noteService = scope.ServiceProvider.GetRequiredService<INoteService>();
        var revisionService = scope.ServiceProvider.GetRequiredService<INoteRevisionService>();

        var current = await noteService.CreateAsync(new CreateNoteDto("Title", "v0"));
        for (var i = 1; i <= INoteRevisionService.MaxRevisionsPerNote + 3; i++)
        {
            current = await noteService.UpdateAsync(new UpdateNoteDto(
                current.Id, "Title", $"v{i}", null, current.Status, current.ParaCategory,
                null, null, null, RowVersion: current.RowVersion));
        }

        var timeline = await revisionService.GetTimelineAsync(current.Id);

        timeline.Count.Should().Be(INoteRevisionService.MaxRevisionsPerNote);
        timeline[0].Content.Should().Be($"v{INoteRevisionService.MaxRevisionsPerNote + 3}");
    }

    /// <summary>
    /// A settable clock that also auto-advances by a tick on every read, so consecutive
    /// SaveChangesAsync calls within a test always get strictly increasing timestamps —
    /// exactly like a real clock — even when a test never explicitly advances <see cref="UtcNow"/>.
    /// Without this, revisions saved in quick succession could tie on CreatedAtUtc and make the
    /// timeline's ordering (and therefore these tests) nondeterministic.
    /// </summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTime _utcNow = DateTime.UtcNow;

        public DateTime UtcNow
        {
            get => _utcNow;
            set => _utcNow = value;
        }

        public override DateTimeOffset GetUtcNow()
        {
            var value = _utcNow;
            _utcNow = _utcNow.AddTicks(1);
            return new DateTimeOffset(value, TimeSpan.Zero);
        }
    }

    private sealed class SqlServerNoteRevisionFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerNoteRevisionFixture(
            string masterConnectionString, string databaseName, ServiceProvider services, MutableTimeProvider time)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            Services = services;
            Time = time;
        }

        public ServiceProvider Services { get; }
        public MutableTimeProvider Time { get; }

        public static async Task<SqlServerNoteRevisionFixture> CreateAsync()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("BRAINY_TEST_SQL_CONNECTIONSTRING");
            var isExplicitlyConfigured = !string.IsNullOrWhiteSpace(configuredConnection);
            if (!isExplicitlyConfigured && OperatingSystem.IsWindows())
            {
                configuredConnection =
                    "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            }

            if (string.IsNullOrWhiteSpace(configuredConnection))
                throw SkipException.ForSkip("Set BRAINY_TEST_SQL_CONNECTIONSTRING to run SQL Server note-revision tests.");

            var databaseName = $"BrainyNoteRevisions_{Guid.NewGuid():N}";
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

            var time = new MutableTimeProvider();
            var services = new ServiceCollection();
            services.AddDbContext<BrainyDbContext>(options =>
                options.UseSqlServer(application.ConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService(UserId));
            services.AddSingleton<TimeProvider>(time);
            services.AddBrainyApplication();

            var provider = services.BuildServiceProvider();
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
                await context.Database.MigrateAsync();
                return new SqlServerNoteRevisionFixture(master.ConnectionString, databaseName, provider, time);
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
                UserName = "note-revision-sql@example.test",
                NormalizedUserName = "NOTE-REVISION-SQL@EXAMPLE.TEST"
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
