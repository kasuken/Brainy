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

namespace Brainy.Web.Tests.Localization;

/// <summary>
/// End-to-end proof of the localization pipeline for the Today surface (issue #323): a user's
/// persisted <see cref="UserDashboardPreference.CultureId"/> is picked up by MainLayout and
/// changes the rendered UI language, with no other behavior change — the empty-state text on a
/// brand-new account (zero tasks) is the simplest surface EmptyTodayExperience.razor exposes.
/// </summary>
public sealed class TodayPageLocalizationRenderTests
{
    [Fact]
    public async Task TodayPage_WithNoCulturePreference_RendersEnglish()
    {
        await using var factory = new TodayLocalizationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        await factory.SeedAsync(cultureId: null);

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("Your horizon is clear.");
        content.Should().NotContain("Il tuo orizzonte");
    }

    [Fact]
    public async Task TodayPage_WithItalianCulturePreference_RendersItalian()
    {
        await using var factory = new TodayLocalizationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        await factory.SeedAsync(cultureId: "it-IT");

        using var response = await client.GetAsync("/today");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        // Blazor's default HtmlEncoder renders accented letters as numeric character
        // references (still "è" in a browser), so compare against the same encoding used
        // elsewhere in this test suite (see SeoProductionSurfaceTests).
        content.Should().Contain(HtmlEncoder.Default.Encode("Il tuo orizzonte è sereno."));
        content.Should().NotContain("Your horizon is clear.");
        content.Should().Contain("<html lang=\"it\"");
    }

    private sealed class TodayLocalizationFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "TodayPageLocalizationRenderTests";
        public const string UserId = "today-localization-user";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("https_port", "443");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["Seo:SiteOrigin"] = "https://localhost",
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
                services.AddSingleton<TimeProvider>(new TimeProviderStub(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero)));
            });
        }

        public async Task SeedAsync(string? cultureId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            if (cultureId is not null)
            {
                db.DashboardPreferences.Add(new UserDashboardPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    CultureId = cultureId
                });
                await db.SaveChangesAsync();
            }
        }

        private sealed class TimeProviderStub(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
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
                [new Claim(ClaimTypes.NameIdentifier, TodayLocalizationFactory.UserId)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
