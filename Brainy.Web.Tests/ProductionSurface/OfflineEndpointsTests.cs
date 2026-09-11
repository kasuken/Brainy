using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

/// <summary>
/// Web-layer coverage for Brainy's Offline Lite (issue #302) JSON endpoints: the read-only
/// Today snapshot and the queued-capture sync endpoint. Mirrors
/// <c>CaptureShareRenderTests</c>'s self-contained WebApplicationFactory + test-auth pattern.
/// </summary>
public sealed class OfflineEndpointsTests
{
    private const string SnapshotUrl = "/api/offline/today-snapshot";
    private const string SyncUrl = "/api/offline/captures/sync";

    [Fact]
    public async Task TodaySnapshot_WithoutAuthentication_IsNotServed()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(SnapshotUrl);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TodaySnapshot_WithCurrentFocusAndFavorite_ReturnsBoth()
    {
        await using var factory = new OfflineEndpointsFactory();
        await factory.SeedAsync(OfflineEndpointsFactory.UserA, async db =>
        {
            var project = new Project
            {
                Id = Guid.NewGuid(), UserId = OfflineEndpointsFactory.UserA, Name = "P",
                Status = ProjectStatus.Active, Priority = ProjectPriority.Medium
            };
            db.Projects.Add(project);
            db.Tasks.Add(new TaskItem
            {
                Id = Guid.NewGuid(), UserId = OfflineEndpointsFactory.UserA, ProjectId = project.Id,
                Title = "Focus task", Status = TaskItemStatus.InProgress, Priority = TaskPriority.Medium,
                IsCurrentTask = true
            });
            db.Notes.Add(new Note
            {
                Id = Guid.NewGuid(), UserId = OfflineEndpointsFactory.UserA, Title = "Fav note",
                Content = "c", Status = NoteStatus.Active, ParaCategory = ParaCategory.Project, IsFavorite = true
            });
            await db.SaveChangesAsync();
        });
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);

        using var response = await client.GetAsync(SnapshotUrl);
        var snapshot = await response.Content.ReadFromJsonAsync<OfflineSnapshotDto>();

        response.EnsureSuccessStatusCode();
        snapshot!.CurrentFocus!.Title.Should().Be("Focus task");
        snapshot.Notes.Should().ContainSingle(n => n.Title == "Fav note");
    }

    [Fact]
    public async Task TodaySnapshot_DoesNotLeakAnotherUsersData()
    {
        await using var factory = new OfflineEndpointsFactory();
        await factory.SeedAsync(OfflineEndpointsFactory.UserB, async db =>
        {
            db.Notes.Add(new Note
            {
                Id = Guid.NewGuid(), UserId = OfflineEndpointsFactory.UserB, Title = "B's note",
                Content = "c", Status = NoteStatus.Active, ParaCategory = ParaCategory.Project, IsFavorite = true
            });
            await db.SaveChangesAsync();
        });
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);

        using var response = await client.GetAsync(SnapshotUrl);
        var snapshot = await response.Content.ReadFromJsonAsync<OfflineSnapshotDto>();

        response.EnsureSuccessStatusCode();
        snapshot!.Notes.Should().BeEmpty();
        snapshot.CurrentFocus.Should().BeNull();
    }

    [Fact]
    public async Task Sync_WithoutAuthentication_IsRejected()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync(SyncUrl, new OfflineCaptureSyncBatchDto([]));

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sync_NewItem_CreatesOneInboxNote()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "captured offline", null)
        ]);

        using var response = await client.PostAsJsonAsync(SyncUrl, batch);
        var result = await response.Content.ReadFromJsonAsync<OfflineCaptureSyncResultDto>();

        response.EnsureSuccessStatusCode();
        result!.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        (await factory.CountNotesForUserAsync(OfflineEndpointsFactory.UserA)).Should().Be(1);
    }

    [Fact]
    public async Task Sync_SameBatchPostedTwice_SyncsExactlyOnce()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "retried after a dropped response", null)
        ]);

        using var first = await client.PostAsJsonAsync(SyncUrl, batch);
        using var second = await client.PostAsJsonAsync(SyncUrl, batch);
        var secondResult = await second.Content.ReadFromJsonAsync<OfflineCaptureSyncResultDto>();

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        secondResult!.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.AlreadySynced);
        (await factory.CountNotesForUserAsync(OfflineEndpointsFactory.UserA)).Should().Be(1);
    }

    [Fact]
    public async Task Sync_OversizedBatch_ReturnsBadRequest()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);
        var items = Enumerable.Range(0, 100)
            .Select(i => new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, $"item {i}", null))
            .ToList();

        using var response = await client.PostAsJsonAsync(SyncUrl, new OfflineCaptureSyncBatchDto(items));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_MalformedItemWithNothingCapturable_IsRejectedWithoutFailingTheBatch()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var client = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);
        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, null, null)
        ]);

        using var response = await client.PostAsJsonAsync(SyncUrl, batch);
        var result = await response.Content.ReadFromJsonAsync<OfflineCaptureSyncResultDto>();

        response.EnsureSuccessStatusCode();
        result!.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Rejected);
        (await factory.CountNotesForUserAsync(OfflineEndpointsFactory.UserA)).Should().Be(0);
    }

    [Fact]
    public async Task Sync_SharesNoNotesBetweenDifferentUsers()
    {
        await using var factory = new OfflineEndpointsFactory();
        using var clientA = factory.AuthenticatedClient(OfflineEndpointsFactory.UserA);
        using var clientB = factory.AuthenticatedClient(OfflineEndpointsFactory.UserB);

        await clientA.PostAsJsonAsync(SyncUrl, new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "A's capture", null)
        ]));
        await clientB.PostAsJsonAsync(SyncUrl, new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, "B's capture", null)
        ]));

        (await factory.CountNotesForUserAsync(OfflineEndpointsFactory.UserA)).Should().Be(1);
        (await factory.CountNotesForUserAsync(OfflineEndpointsFactory.UserB)).Should().Be(1);
    }

    private sealed class OfflineEndpointsFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"OfflineEndpointsTests-{Guid.NewGuid()}";
        public const string UserA = "offline-web-user-a";
        public const string UserB = "offline-web-user-b";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("https_port", "443");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=127.0.0.1,1;Database=BrainyTests;User Id=test;Password=test;TrustServerCertificate=true;Connect Timeout=1"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<BrainyDbContext>>();
                services.RemoveAll<BrainyDbContext>();
                services.RemoveAll<IApplicationDbContext>();

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

                var efProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<BrainyDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName)
                        .UseInternalServiceProvider(efProvider));
                services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            });
        }

        /// <summary>An HttpClient pre-authenticated (via the test auth handler) as <paramref name="userId"/>.</summary>
        public HttpClient AuthenticatedClient(string userId)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
            return client;
        }

        public async Task SeedAsync(string userId, Func<BrainyDbContext, Task> seed)
        {
            _ = userId;
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await seed(db);
        }

        public async Task<int> CountNotesForUserAsync(string userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            return await db.Notes.CountAsync(n => n.UserId == userId);
        }
    }

    /// <summary>
    /// Authenticates every request as the user named in the <c>X-Test-User</c> header (or
    /// <see cref="OfflineEndpointsFactory.UserA"/> when absent), so a single in-process server
    /// can exercise per-user isolation across multiple simulated users.
    /// </summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestAuth";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Unlike CaptureShareRenderTests's handler (which only ever exercises
            // authenticated scenarios), this suite also needs a genuinely anonymous case —
            // so, unlike that one, a missing header means "not authenticated" rather than
            // falling back to a default user.
            if (!Context.Request.Headers.TryGetValue(UserHeader, out var values) || values.Count == 0)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, values[0]!)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
