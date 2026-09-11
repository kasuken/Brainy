using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data;
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
/// Web-layer coverage for the minimal capture route (issue #298): the PWA share-target
/// GET handler at <c>/capture/share</c>.
/// </summary>
public sealed class CaptureShareRenderTests
{
    [Fact]
    public async Task SharedUrlAndText_CreatesExactlyOneInboxNote()
    {
        await using var factory = new CaptureSharePageFactory();
        using var client = factory.AuthenticatedClient(CaptureSharePageFactory.UserA);

        using var response = await client.GetAsync(
            "/capture/share?title=Great%20article&text=worth%20reading%20later&url=https%3A%2F%2Fexample.com%2Fa");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("Saved to Inbox");
        (await factory.CountNotesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RepeatedIdenticalShareRequest_DoesNotCreateASecondNote()
    {
        await using var factory = new CaptureSharePageFactory();
        using var client = factory.AuthenticatedClient(CaptureSharePageFactory.UserA);
        const string path = "/capture/share?title=Retry&text=flaky%20share%20sheet&url=https%3A%2F%2Fexample.com%2Fr";

        using var first = await client.GetAsync(path);
        using var second = await client.GetAsync(path);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        (await second.Content.ReadAsStringAsync()).Should().Contain("Saved to Inbox");
        (await factory.CountNotesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OversizedSharedText_ShowsFriendlyErrorAndSavesNothing()
    {
        await using var factory = new CaptureSharePageFactory();
        using var client = factory.AuthenticatedClient(CaptureSharePageFactory.UserA);
        var oversizedText = new string('a', IShareCaptureService.MaxTextLength + 1);

        using var response = await client.GetAsync($"/capture/share?text={oversizedText}");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().NotContain("Saved to Inbox");
        content.Should().Contain("didn't go through");
        (await factory.CountNotesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SharesFromDifferentUsers_AreOwnedBySeparateUsersNotEachOther()
    {
        await using var factory = new CaptureSharePageFactory();
        using var clientA = factory.AuthenticatedClient(CaptureSharePageFactory.UserA);
        using var clientB = factory.AuthenticatedClient(CaptureSharePageFactory.UserB);
        const string path = "/capture/share?text=identical%20shared%20wording";

        await clientA.GetAsync(path);
        await clientB.GetAsync(path);

        (await factory.CountNotesAsync()).Should().Be(2);
        (await factory.CountNotesForUserAsync(CaptureSharePageFactory.UserA)).Should().Be(1);
        (await factory.CountNotesForUserAsync(CaptureSharePageFactory.UserB)).Should().Be(1);
    }

    [Fact]
    public async Task VisitingWithNoShareParameters_RendersTheManualCaptureFormInstead()
    {
        await using var factory = new CaptureSharePageFactory();
        using var client = factory.AuthenticatedClient(CaptureSharePageFactory.UserA);

        using var response = await client.GetAsync("/capture/share");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().NotContain("Saved to Inbox");
        content.Should().Contain("Capture a thought");
        (await factory.CountNotesAsync()).Should().Be(0);
    }

    private sealed class CaptureSharePageFactory : WebApplicationFactory<Program>
    {
        // Computed once per factory instance (one per test) rather than inside the
        // AddDbContext options callback, which AddDbContext invokes anew for every scope
        // — a fresh Guid there would give each request its own empty in-memory database.
        private readonly string _databaseName = $"CaptureShareRenderTests-{Guid.NewGuid()}";
        public const string UserA = "capture-web-user-a";
        public const string UserB = "capture-web-user-b";

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

        public async Task<int> CountNotesAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            return await db.Notes.CountAsync();
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
    /// <see cref="CaptureSharePageFactory.UserA"/> when absent), so a single in-process
    /// server can exercise per-user isolation across multiple simulated users.
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
            var userId = Context.Request.Headers.TryGetValue(UserHeader, out var values) && values.Count > 0
                ? values[0]!
                : CaptureSharePageFactory.UserA;

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
