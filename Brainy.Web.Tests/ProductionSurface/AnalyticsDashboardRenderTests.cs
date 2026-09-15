using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
using Brainy.Data.Identity;
using Brainy.Domain.Entities;
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
/// End-to-end markup and endpoint tests for the activation-funnel work on the internal
/// analytics dashboard (issue #324): the funnel renders with per-step conversion, the
/// consent-gap denominator is disclosed rather than silently undercounted, and the CSV
/// export is gated behind the same admin allowlist as the dashboard page itself.
/// </summary>
public sealed class AnalyticsDashboardRenderTests
{
    private const string AdminEmail = "admin@brainy.test";
    private const string OtherUserEmail = "not-admin@brainy.test";
    private const string AdminUserId = "analytics-dashboard-admin";

    [Fact]
    public async Task AuthorizedAdmin_SeesFunnelWithConsentGapDisclosureAndExportLink()
    {
        await using var factory = new AnalyticsDashboardTestFactory(callerEmail: AdminEmail);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();
        await factory.SeedAsync(registeredUserCount: 2, optedOutUserId: "opted-out-user");

        using var response = await client.GetAsync("/admin/analytics");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        // Full funnel, in order, with the new "first output" step.
        content.Should().Contain("Activation funnel");
        content.Should().Contain("First capture");
        content.Should().Contain("First classify");
        content.Should().Contain("First task created");
        content.Should().Contain("First current-focus selection");
        content.Should().Contain("First output created");

        // The consent gap must be disclosed, not silently folded into the denominator.
        content.Should().Contain("known to have");
        content.Should().Contain("opted out of analytics");
        content.Should().Contain("floor, not a silently-undercounted whole population");

        // New aggregate sections and the CSV export entry point.
        content.Should().Contain("Knowledge reuse");
        content.Should().Contain("Weekly review &amp; Inbox processing adherence");
        content.Should().Contain("/api/analytics/export.csv");
        content.Should().Contain("Export CSV");
    }

    [Fact]
    public async Task ExportCsv_WhenAuthorized_ReturnsAggregateCsv()
    {
        await using var factory = new AnalyticsDashboardTestFactory(callerEmail: AdminEmail);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();
        await factory.SeedAsync(registeredUserCount: 3, optedOutUserId: null);

        using var response = await client.GetAsync("/api/analytics/export.csv");
        var csv = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csv.Should().StartWith("section,metric,value");
        csv.Should().Contain("activation_funnel,registered_users,3");

        // Aggregate only: no per-user identifier anywhere in the exported file.
        csv.Should().NotContain(AdminUserId);
    }

    [Fact]
    public async Task ExportCsv_WhenCallerIsNotAnAdmin_ReturnsNotFound()
    {
        await using var factory = new AnalyticsDashboardTestFactory(callerEmail: OtherUserEmail);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();

        using var response = await client.GetAsync("/api/analytics/export.csv");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class AnalyticsDashboardTestFactory(string callerEmail) : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "AnalyticsDashboardRenderTests";

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
                        "Server=127.0.0.1,1;Database=BrainyTests;User Id=test;Password=test;TrustServerCertificate=true;Connect Timeout=1",
                    ["Analytics:AdminEmails:0"] = AdminEmail
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
                    .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                        TestAuthHandler.Scheme,
                        options => options.CallerEmail = callerEmail);

                var efProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<BrainyDbContext>(options =>
                    options.UseInMemoryDatabase(DatabaseName)
                        .UseInternalServiceProvider(efProvider));
                services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            });
        }

        public async Task ResetDatabaseAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        /// <summary>
        /// Seeds <paramref name="registeredUserCount"/> Identity users (so
        /// <c>IUserDirectoryService</c> reports a real registered-user total) and, when
        /// given, opts one of them out of analytics via a <see cref="UserDashboardPreference"/>
        /// row, so the dashboard has a known consent gap to disclose.
        /// </summary>
        public async Task SeedAsync(int registeredUserCount, string? optedOutUserId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            for (var i = 0; i < registeredUserCount; i++)
            {
                var userId = i == 0 ? AdminUserId : $"seeded-user-{i}";
                db.Users.Add(new ApplicationUser
                {
                    Id = userId,
                    UserName = $"{userId}@brainy.test",
                    NormalizedUserName = $"{userId}@brainy.test".ToUpperInvariant(),
                    Email = $"{userId}@brainy.test",
                    NormalizedEmail = $"{userId}@brainy.test".ToUpperInvariant(),
                });
            }

            if (optedOutUserId is not null)
            {
                db.DashboardPreferences.Add(new UserDashboardPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = optedOutUserId,
                    AnalyticsEnabled = false
                });
            }

            await db.SaveChangesAsync();
        }
    }

    private sealed class TestAuthHandlerOptions : AuthenticationSchemeOptions
    {
        public string CallerEmail { get; set; } = "";
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<TestAuthHandlerOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                    new Claim(ClaimTypes.Email, Options.CallerEmail)
                ],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
