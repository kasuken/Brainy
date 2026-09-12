using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
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

namespace Brainy.Web.Tests.CommandPalette;

/// <summary>
/// Issue #318 adds creation/contextual-action commands that navigate to an existing
/// page with a query flag (e.g. <c>/projects?new=true</c>, <c>/today?action=set-focus</c>),
/// which the page's own <c>OnAfterRenderAsync</c> reads to auto-open the same dialog its
/// "New …" button would. These tests only prove the query-bound page still renders
/// successfully with the flag present (a real interactive circuit — required to actually
/// open a MudBlazor dialog — is not available under <see cref="WebApplicationFactory{T}"/>'s
/// plain HTTP client, mirroring how the pre-existing <c>/notes?open=&lt;id&gt;</c> deep link
/// is tested elsewhere in this project).
/// </summary>
public sealed class CommandPaletteQueryFlagRenderTests
{
    [Theory]
    [InlineData("/today")]
    [InlineData("/today?action=set-focus")]
    [InlineData("/today?action=new-task")]
    [InlineData("/today?action=unknown-value-is-ignored")]
    [InlineData("/projects")]
    [InlineData("/projects?new=true")]
    [InlineData("/outputs")]
    [InlineData("/outputs?new=true")]
    [InlineData("/notes")]
    [InlineData("/notes?new=true")]
    public async Task PageWithPaletteQueryFlag_StillRendersSuccessfully(string requestUri)
    {
        await using var factory = new CommandPaletteQueryFlagFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        await factory.ResetDatabaseAsync();

        using var response = await client.GetAsync(requestUri);
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class CommandPaletteQueryFlagFactory : WebApplicationFactory<Program>
    {
        private const string DatabaseName = "CommandPaletteQueryFlagRenderTests";
        public const string UserId = "palette-query-flag-web-user";

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
                [new Claim(ClaimTypes.NameIdentifier, CommandPaletteQueryFlagFactory.UserId)],
                Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
