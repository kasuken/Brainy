using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
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
/// Markup tests for the Starter Mode + guided onboarding journey from issue #294:
/// a fresh account sees the dismissible onboarding entry point and a trimmed nav,
/// while an onboarded / opted-out account does not.
/// </summary>
public sealed class OnboardingJourneyRenderTests
{
    [Fact]
    public async Task FreshAccount_SeesOnboardingJourneyAndStarterModeNav()
    {
        await using var factory = new OnboardingTestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        // Onboarding journey renders, shows step 1 of 6, and offers a dismiss control.
        content.Should().Contain("Getting started guide");
        content.Should().Contain("Step 1 of 6");
        content.Should().Contain("Skip the getting-started guide");
        content.Should().Contain("Skip the tour");

        // Starter Mode trims the *nav drawer* to Today / Inbox / Projects / Search by
        // default. Matched against the nav-item wrapper specifically: "/tasks-hub" and
        // "/pulse" still legitimately appear elsewhere on Today (e.g. the daily snapshot
        // strip and the Today/Week/Tasks Hub explainer) — those are not nav-menu items.
        content.Should().Contain("Show full navigation");
        content.Should().NotContain("<div class=\"mud-nav-item\"><a href=\"/tasks-hub\"");
        content.Should().NotContain("<div class=\"mud-nav-item\"><a href=\"/pulse\"");
    }

    [Fact]
    public async Task OnboardedAccount_DoesNotSeeOnboardingJourney()
    {
        await using var factory = new OnboardingTestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();
        await factory.SetPreferenceAsync(onboardingCompleted: true, starterModeEnabled: true);

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().NotContain("Getting started guide");
    }

    [Fact]
    public async Task StarterModeDisabled_ShowsFullNavigation()
    {
        await using var factory = new OnboardingTestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();
        await factory.SetPreferenceAsync(onboardingCompleted: true, starterModeEnabled: false);

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("<div class=\"mud-nav-item\"><a href=\"/tasks-hub\"");
        content.Should().Contain("<div class=\"mud-nav-item\"><a href=\"/pulse\"");
        content.Should().NotContain("Show full navigation");
    }

    private sealed class OnboardingTestFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "OnboardingJourneyRenderTests";
        public const string UserId = "onboarding-web-user";

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

        public async Task SetPreferenceAsync(bool onboardingCompleted, bool starterModeEnabled)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            db.DashboardPreferences.Add(new UserDashboardPreference
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                OnboardingCompleted = onboardingCompleted,
                StarterModeEnabled = starterModeEnabled
            });
            await db.SaveChangesAsync();
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, OnboardingTestFactory.UserId)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
