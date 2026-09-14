using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>
/// Boots a real, Kestrel-hosted instance of Brainy.Web (not the in-memory <c>TestServer</c>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> normally uses) so a real browser can
/// navigate to it. See <see cref="BrainyE2EFixture"/> for the database and Playwright wiring
/// around this factory.
/// </summary>
public sealed class BrainyAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public BrainyAppFactory(string connectionString)
    {
        _connectionString = connectionString;

        // Port 0 = let the OS pick a free port; the real bound address is read back from
        // ClientOptions.BaseAddress (via IServerAddressesFeature) once the host starts.
        UseKestrel(0);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development (not Production) so cookies use SameAsRequest instead of Secure-only —
        // this factory serves plain HTTP, and antiforgery/Identity cookies would otherwise be
        // silently dropped by the browser. See BrainyWebApplicationFactory in Brainy.Web.Tests
        // for the same reasoning on the existing production-surface tests.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Migrations are applied explicitly and once by BrainyE2EFixture before the
                // host starts (see its InitializeAsync) — turning this off here also skips
                // Program.cs's IsDevelopment()-gated DevelopmentDataSeeder, so every test
                // starts from a schema-only database with no demo user/data to collide with.
                // (This key is read AFTER WebApplicationBuilder.Build(), so ConfigureAppConfiguration
                // — applied at Build() time — reaches it in time; see the ConfigureHostConfiguration
                // override below for the one setting that is read too early for this to work.)
                ["Database:ApplyMigrationsOnStartup"] = "false",

                ["Identity:AllowRegistration"] = "true",
                ["Identity:RequireConfirmedAccount"] = "false",

                // Only used for canonical/absolute link generation (sitemap, OG tags); not
                // required to match the real Kestrel address, and Development skips the
                // "must be https" validation applied in Production.
                ["Seo:SiteOrigin"] = "http://127.0.0.1",

                // Belt-and-braces: these already default to "None" in appsettings.json (see
                // AGENTS.md / the issue's guardrails), but pinned explicitly here so the E2E
                // suite can never accidentally call a real AI provider, send real email,
                // reach Stripe, or dispatch a real push notification, regardless of what a
                // future change to appsettings.json's defaults might do.
                ["AiAssistant:Provider"] = "None",
                ["Billing:Provider"] = "None",
                ["Email:Provider"] = "None",
                ["WebPush:VapidPublicKey"] = "",
                ["WebPush:VapidPrivateKey"] = "",
                ["Telemetry:Enabled"] = "false",
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Brainy.Data.DependencyInjection.AddBrainyData reads ConnectionStrings:DefaultConnection
        // eagerly (a plain string captured once, at `builder.Services.AddBrainyData(...)` inside
        // Program.cs — i.e. BEFORE WebApplicationBuilder.Build() runs). ConfigureWebHost's
        // ConfigureAppConfiguration (above) only becomes visible at Build() time, which is too
        // late for that eager read — it would still see appsettings.json's LocalDB connection
        // string. ConfigureHostConfiguration folds a value in earlier (effectively as if it were
        // passed as an argument to WebApplication.CreateBuilder), which the eager read does see.
        // See the "Customize the WebApplicationFactory with test configurations" section of
        // https://learn.microsoft.com/aspnet/core/test/integration-tests for this distinction.
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            });
        });

        return base.CreateHost(builder);
    }
}
