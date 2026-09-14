using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Data;
using Brainy.Data.Identity;
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
/// Web-layer, end-to-end coverage for the anonymous output share page (issue #319):
/// <c>GET /share?token=...</c>. Unlike <see cref="OfflineEndpointsTests"/>, there is no cookie
/// auth here at all — the share token in the query string is the entire credential, so these
/// tests seed <see cref="OutputShareLink"/> rows directly (hashing a fixed raw token exactly as
/// <c>OutputShareLinkService</c> does) rather than logging in as anyone. Also proves, end to
/// end, the acceptance criteria that matter most for this issue: a revoked/expired/unknown
/// token is a clean 404, the page is always noindex, and the rendered HTML carries nothing
/// beyond the shared output's own title/description/content.
/// </summary>
public sealed class OutputShareEndpointsTests
{
    private const string ShareUrl = "/share";

    private static HttpClient CreateClient(OutputShareFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    [Fact]
    public async Task Get_WithoutAToken_ReturnsNotFound()
    {
        await using var factory = new OutputShareFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(ShareUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithAnUnknownToken_ReturnsNotFound()
    {
        await using var factory = new OutputShareFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={new string('a', 64)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("not-hex-at-all-not-hex-at-all-not-hex-at-all-not-hex-at-all-00")]
    [InlineData("short")]
    public async Task Get_WithAMalformedToken_ReturnsNotFoundRatherThanThrowing(string garbage)
    {
        await using var factory = new OutputShareFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={Uri.EscapeDataString(garbage)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithAValidToken_RendersTheOutputsMarkdownReadOnly()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkAsync(
            OutputShareFactory.UserA,
            "Shareable Title",
            "A safe public description",
            "# Heading\n\nSome **body** content.");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={token}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Shareable Title");
        body.Should().Contain("A safe public description");
        body.Should().Contain("Some <strong>body</strong> content.");
    }

    [Fact]
    public async Task Get_WithAValidToken_IsAlwaysNoIndex()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkAsync(OutputShareFactory.UserA, "Indexable?", null, "Body.");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={token}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("<meta name=\"robots\" content=\"noindex, nofollow\"");
    }

    [Fact]
    public async Task Get_LeaksNothingBeyondTheOutputsOwnContent()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkWithSurroundingDataAsync();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={token}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(OutputShareFactory.SharedOutputTitle);

        // Nothing about the owner's account, or any other of the owner's data, may leak onto
        // an anonymous, unauthenticated page — this is #319's central guardrail.
        body.Should().NotContain(OutputShareFactory.OwnerEmail);
        body.Should().NotContain(OutputShareFactory.OwnerUserId);
        body.Should().NotContain(OutputShareFactory.SecretProjectName);
        body.Should().NotContain(OutputShareFactory.SecretTaskTitle);
        body.Should().NotContain(OutputShareFactory.SecretNoteTitle);
        body.Should().NotContain(OutputShareFactory.SecretNoteContent);
        body.Should().NotContain(OutputShareFactory.SecretAreaName);
        body.Should().NotContain(OutputShareFactory.SecretGoalTitle);
    }

    [Fact]
    public async Task Get_AfterRevoke_NoLongerServesThePage()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkAsync(OutputShareFactory.UserA, "Title", null, "Body");
        using var client = CreateClient(factory);

        using var beforeRevoke = await client.GetAsync($"{ShareUrl}?token={token}");
        await factory.RevokeAsync(OutputShareFactory.UserA);
        using var afterRevoke = await client.GetAsync($"{ShareUrl}?token={token}");

        beforeRevoke.StatusCode.Should().Be(HttpStatusCode.OK);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound, "revoking must break the link on the very next request");
    }

    [Fact]
    public async Task Get_AfterExpiry_ReturnsNotFound()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkAsync(
            OutputShareFactory.UserA, "Title", null, "Body",
            expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "an expired link must be a clean 404, exactly like a revoked one");
    }

    [Fact]
    public async Task Get_WithUserAsToken_NeverRendersUserBsOutput()
    {
        await using var factory = new OutputShareFactory();
        var tokenA = await factory.SeedShareLinkAsync(OutputShareFactory.UserA, "Output Alpha shared", null, "Alpha body");
        await factory.SeedShareLinkAsync(OutputShareFactory.UserB, "Output Beta shared", null, "Beta body");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={tokenA}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Output Alpha shared");
        body.Should().NotContain("Output Beta shared");
    }

    [Fact]
    public async Task Get_NeverEchoesTheTokenInAResponseHeader()
    {
        await using var factory = new OutputShareFactory();
        var token = await factory.SeedShareLinkAsync(OutputShareFactory.UserA, "Title", null, "Body");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"{ShareUrl}?token={token}");

        response.Headers.Concat(response.Content.Headers)
            .Should().NotContain(h => h.Value.Any(v => v.Contains(token)));
    }

    [Fact]
    public async Task Get_PolledMoreThanThePermitLimit_Returns429()
    {
        // Program.cs partitions the share page's rate limit by (a hash of) the token itself,
        // so this works with a token that never resolves to anyone — the limiter runs ahead
        // of, and independently of, token resolution.
        await using var factory = new OutputShareFactory();
        using var client = CreateClient(factory);
        var token = new string('b', 64);

        HttpStatusCode? rateLimited = null;
        for (var i = 0; i < 35 && rateLimited is null; i++)
        {
            using var response = await client.GetAsync($"{ShareUrl}?token={token}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rateLimited = response.StatusCode;
        }

        rateLimited.Should().Be(HttpStatusCode.TooManyRequests, "the share page must reject aggressive polling/guessing once its rate limit is exceeded");
    }

    private sealed class OutputShareFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"OutputShareEndpointsTests-{Guid.NewGuid()}";
        public const string UserA = "output-share-web-user-a";
        public const string UserB = "output-share-web-user-b";

        public const string OwnerEmail = "output-share-owner@example.test";
        public const string SharedOutputTitle = "The Shareable Output";
        public const string SecretProjectName = "Confidential Project Zeta";
        public const string SecretTaskTitle = "Draft the confidential memo";
        public const string SecretNoteTitle = "Private note about the acquisition";
        public const string SecretNoteContent = "Nobody outside the company may see this note body.";
        public const string SecretAreaName = "Executive Operations";
        public const string SecretGoalTitle = "Close the confidential deal";
        public static readonly string OwnerUserId = Guid.NewGuid().ToString();

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
        /// Seeds one output for <paramref name="userId"/> and a share link whose hash is
        /// computed exactly as <c>OutputShareLinkService</c> computes it, returning the raw
        /// token to use on the query string.
        /// </summary>
        public async Task<string> SeedShareLinkAsync(
            string userId,
            string outputTitle,
            string? outputDescription,
            string outputContent,
            DateTime? expiresAtUtc = null)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            var output = new Output
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = outputTitle,
                Description = outputDescription,
                Content = outputContent,
                Type = OutputType.Report,
                Status = OutputStatus.Draft
            };
            db.Outputs.Add(output);
            db.OutputShareLinks.Add(NewShareLink(userId, output.Id, out var rawToken, expiresAtUtc));
            await db.SaveChangesAsync();

            return rawToken;
        }

        /// <summary>
        /// Seeds a full, realistic account for <see cref="UserA"/> — a project, task, note,
        /// area, and goal, none of which the shared output references — plus the one output
        /// that is actually shared, so <see cref="Get_LeaksNothingBeyondTheOutputsOwnContent"/>
        /// can assert none of that surrounding data ever reaches the anonymous response.
        /// </summary>
        public async Task<string> SeedShareLinkWithSurroundingDataAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            db.Users.Add(new ApplicationUser
            {
                Id = OwnerUserId,
                UserName = OwnerEmail,
                NormalizedUserName = OwnerEmail.ToUpperInvariant(),
                Email = OwnerEmail,
                NormalizedEmail = OwnerEmail.ToUpperInvariant()
            });

            var area = new Area { Id = Guid.NewGuid(), UserId = OwnerUserId, Name = SecretAreaName };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                UserId = OwnerUserId,
                Name = SecretProjectName,
                Status = ProjectStatus.Active,
                Priority = ProjectPriority.Medium
            };
            var goal = new Goal
            {
                Id = Guid.NewGuid(),
                UserId = OwnerUserId,
                Title = SecretGoalTitle,
                Status = GoalStatus.Active
            };
            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = OwnerUserId,
                ProjectId = project.Id,
                Title = SecretTaskTitle,
                Status = TaskItemStatus.Todo,
                Priority = TaskPriority.Medium
            };
            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = OwnerUserId,
                Title = SecretNoteTitle,
                Content = SecretNoteContent,
                Status = NoteStatus.Active,
                ParaCategory = ParaCategory.Resource
            };

            var output = new Output
            {
                Id = Guid.NewGuid(),
                UserId = OwnerUserId,
                Title = SharedOutputTitle,
                Description = "A description that is safe to share publicly.",
                Content = "# Public content\n\nThis is the only thing that should render.",
                Type = OutputType.Report,
                Status = OutputStatus.Draft,
                ProjectId = project.Id,
                AreaId = area.Id,
                GoalId = goal.Id,
                SourceNotes = [note]
            };

            db.AddRange(area, project, goal, task, note, output);
            db.OutputShareLinks.Add(NewShareLink(OwnerUserId, output.Id, out var rawToken));
            await db.SaveChangesAsync();

            return rawToken;
        }

        public async Task RevokeAsync(string userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
            var link = await db.OutputShareLinks.SingleAsync(l =>
                db.Outputs.Any(o => o.Id == l.OutputId && o.UserId == userId));
            link.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        private static OutputShareLink NewShareLink(
            string userId, Guid outputId, out string rawToken, DateTime? expiresAtUtc = null)
        {
            rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            return new OutputShareLink
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OutputId = outputId,
                TokenHash = tokenHash,
                ExpiresAtUtc = expiresAtUtc
            };
        }
    }
}
