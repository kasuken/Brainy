using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

/// <summary>
/// Web-layer, end-to-end coverage for the anonymous ICS feed endpoint (issue #314):
/// <c>GET /api/calendar/feed.ics</c>. Unlike <see cref="OfflineEndpointsTests"/>, there is no
/// cookie auth here at all — the feed token in the query string is the entire credential, so
/// these tests seed <see cref="CalendarFeedToken"/> rows directly (hashing a fixed raw token
/// exactly as <c>CalendarFeedTokenService</c> does) rather than logging in as anyone.
/// </summary>
public sealed class CalendarFeedEndpointsTests
{
    private const string FeedUrl = "/api/calendar/feed.ics";

    private static HttpClient CreateClient(CalendarFeedFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    [Fact]
    public async Task Get_WithoutAToken_ReturnsNotFound()
    {
        await using var factory = new CalendarFeedFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(FeedUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithAnUnknownToken_ReturnsNotFound()
    {
        await using var factory = new CalendarFeedFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{FeedUrl}?token={new string('a', 64)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithAValidToken_ReturnsThatUsersEventsAsIcs()
    {
        await using var factory = new CalendarFeedFactory();
        var token = await factory.SeedTokenAndDeadlineAsync(CalendarFeedFactory.UserA, "A's task");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{FeedUrl}?token={token}");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        body.Should().Contain("BEGIN:VCALENDAR").And.Contain("BEGIN:VEVENT").And.Contain("A's task");
    }

    [Fact]
    public async Task Get_WithUserAsToken_NeverReturnsUserBsEvents()
    {
        await using var factory = new CalendarFeedFactory();
        var tokenA = await factory.SeedTokenAndDeadlineAsync(CalendarFeedFactory.UserA, "A's task");
        await factory.SeedTokenAndDeadlineAsync(CalendarFeedFactory.UserB, "B's task");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{FeedUrl}?token={tokenA}");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        body.Should().Contain("A's task");
        body.Should().NotContain("B's task");
    }

    [Fact]
    public async Task Get_AfterRevoke_NoLongerServesTheFeed()
    {
        await using var factory = new CalendarFeedFactory();
        var token = await factory.SeedTokenAndDeadlineAsync(CalendarFeedFactory.UserA, "A's task");
        using var client = CreateClient(factory);

        using var beforeRevoke = await client.GetAsync($"{FeedUrl}?token={token}");
        await factory.RevokeAsync(CalendarFeedFactory.UserA);
        using var afterRevoke = await client.GetAsync($"{FeedUrl}?token={token}");

        beforeRevoke.StatusCode.Should().Be(HttpStatusCode.OK);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound, "revoking must break the subscription on the very next request");
    }

    [Fact]
    public async Task Get_PolledMoreThanThePermitLimit_Returns429()
    {
        // Program.cs partitions the feed's rate limit by (a hash of) the token itself, so
        // this works with a token that never resolves to anyone — the limiter runs ahead of,
        // and independently of, token validation.
        await using var factory = new CalendarFeedFactory();
        using var client = CreateClient(factory);
        var token = new string('b', 64);

        HttpStatusCode? rateLimited = null;
        for (var i = 0; i < 35 && rateLimited is null; i++)
        {
            using var response = await client.GetAsync($"{FeedUrl}?token={token}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rateLimited = response.StatusCode;
        }

        rateLimited.Should().Be(HttpStatusCode.TooManyRequests, "the feed endpoint must reject aggressive polling once its rate limit is exceeded");
    }

    [Fact]
    public async Task Get_NeverEchoesTheTokenInAResponseHeader()
    {
        // The token must never leak via any transport surface an operator might log
        // (headers included) — see PrivacyRedactionProcessor's url.query redaction, which
        // this endpoint relies on by keeping the token in the query string, never a path
        // segment or a response header.
        await using var factory = new CalendarFeedFactory();
        var token = await factory.SeedTokenAndDeadlineAsync(CalendarFeedFactory.UserA, "A's task");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{FeedUrl}?token={token}");

        response.Headers.Concat(response.Content.Headers)
            .Should().NotContain(h => h.Value.Any(v => v.Contains(token)));
    }

    private sealed class CalendarFeedFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"CalendarFeedEndpointsTests-{Guid.NewGuid()}";
        public const string UserA = "calendar-feed-web-user-a";
        public const string UserB = "calendar-feed-web-user-b";

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

                var efProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<BrainyDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName)
                        .UseInternalServiceProvider(efProvider));
                services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
            });
        }

        /// <summary>
        /// Seeds one active project deadline for <paramref name="userId"/> and a feed token
        /// whose hash is computed exactly as <c>CalendarFeedTokenService</c> computes it,
        /// returning the raw token to use on the query string.
        /// </summary>
        public async Task<string> SeedTokenAndDeadlineAsync(string userId, string projectName)
        {
            var rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            db.Projects.Add(new Project
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = projectName,
                Status = ProjectStatus.Active,
                Priority = ProjectPriority.Medium,
                DueDate = new DateTime(2026, 10, 1)
            });
            db.CalendarFeedTokens.Add(new CalendarFeedToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash
            });
            await db.SaveChangesAsync();

            return rawToken;
        }

        public async Task RevokeAsync(string userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            var token = await db.CalendarFeedTokens.SingleAsync(t => t.UserId == userId);
            token.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
