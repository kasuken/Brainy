using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using Xunit;

namespace Brainy.Web.Tests.Mcp;

/// <summary>
/// End-to-end coverage of the MCP endpoint (Phase 1, Step 3) through the real HTTP pipeline:
/// authentication, authorization, and the <c>search</c> tool. The security gate: a valid MCP
/// bearer token reaches the tool and sees only its owner's data, and no/invalid token is
/// rejected before any tool runs. Complements <see cref="McpAuthenticationHandlerTests"/>,
/// which covers token resolution at the handler boundary.
/// </summary>
public sealed class McpServerEndpointTests
{
    private const string McpUrl = "/api/mcp";

    [Fact]
    public async Task Post_WithoutABearerToken_IsRejectedWithABearerChallenge()
    {
        await using var factory = new McpFactory();
        using var client = CreateHttpClient(factory);

        using var response = await PostInitializeAsync(client, bearerToken: null);

        // The MCP authorization policy rejects the request before any tool runs; the Streamable
        // HTTP transport surfaces the rejected request as a 4xx, and the BrainyMcp scheme's
        // challenge sets WWW-Authenticate: Bearer.
        ((int)response.StatusCode).Should().BeInRange(400, 499);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }

    [Fact]
    public async Task Connect_WithoutABearerToken_Fails()
    {
        await using var factory = new McpFactory();

        var connect = async () => await ConnectAsync(factory, bearerToken: null);

        // No token: the initialize handshake is rejected, so a session can never be established.
        await connect.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Connect_WithAnInvalidBearerToken_Fails()
    {
        await using var factory = new McpFactory();

        var connect = async () => await ConnectAsync(
            factory, $"{McpFactory.TokenPrefix}{new string('a', 64)}");

        await connect.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Sends a protocol-correct Streamable-HTTP <c>initialize</c> request (with the
    /// <c>Accept</c> headers the transport requires) so the request reaches the authorization
    /// stage rather than being rejected earlier for its shape.
    /// </summary>
    private static async Task<HttpResponseMessage> PostInitializeAsync(HttpClient client, string? bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpUrl)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "brainy-tests", version = "1.0.0" }
                }
            })
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (bearerToken is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Search_WithAValidToken_ReturnsThatUsersNote()
    {
        await using var factory = new McpFactory();
        var token = await factory.SeedTokenAndNoteAsync(McpFactory.UserA, "Alpha note about kestrels");
        await using var mcpClient = await ConnectAsync(factory, token);

        var result = await mcpClient.CallToolAsync(
            "search",
            new Dictionary<string, object?> { ["query"] = "kestrels" });

        result.IsError.Should().NotBe(true);
        Serialize(result).Should().Contain("Alpha note about kestrels");
    }

    [Fact]
    public async Task Search_WithUserAsToken_NeverReturnsUserBsNotes()
    {
        await using var factory = new McpFactory();
        var tokenA = await factory.SeedTokenAndNoteAsync(McpFactory.UserA, "Alpha private roadmap");
        await factory.SeedTokenAndNoteAsync(McpFactory.UserB, "Bravo private roadmap");
        await using var mcpClient = await ConnectAsync(factory, tokenA);

        var result = await mcpClient.CallToolAsync(
            "search",
            new Dictionary<string, object?> { ["query"] = "roadmap" });

        var payload = Serialize(result);
        payload.Should().Contain("Alpha private roadmap");
        payload.Should().NotContain("Bravo private roadmap", "a token must only ever surface its own owner's data");
    }

    [Fact]
    public async Task ListTools_ExposesTheSearchTool()
    {
        await using var factory = new McpFactory();
        var token = await factory.SeedTokenAndNoteAsync(McpFactory.UserA, "Anything");
        await using var mcpClient = await ConnectAsync(factory, token);

        var tools = await mcpClient.ListToolsAsync();

        tools.Should().Contain(t => t.Name == "search");
    }

    private static HttpClient CreateHttpClient(McpFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<McpClient> ConnectAsync(McpFactory factory, string? bearerToken)
    {
        var httpClient = CreateHttpClient(factory);
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://localhost/api/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        };
        if (bearerToken is not null)
        {
            options.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {bearerToken}"
            };
        }

        var transport = new HttpClientTransport(
            options,
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    private static string Serialize(object result) =>
        JsonSerializer.Serialize(result);

    private sealed class McpFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"McpServerEndpointTests-{Guid.NewGuid()}";
        public const string UserA = "mcp-endpoint-user-a";
        public const string UserB = "mcp-endpoint-user-b";
        public const string TokenPrefix = "brainy_mcp_";

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
        /// Seeds one non-archived note for <paramref name="userId"/> and an active MCP token
        /// whose hash is computed exactly as <c>McpAccessTokenService</c> computes it,
        /// returning the raw token to present as a bearer.
        /// </summary>
        public async Task<string> SeedTokenAndNoteAsync(string userId, string noteTitle)
        {
            var rawToken = TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();

            db.Notes.Add(new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = noteTitle,
                Content = "Seeded content for MCP search testing.",
                Status = NoteStatus.Active,
                ParaCategory = ParaCategory.Resource,
                IsArchived = false
            });
            db.McpAccessTokens.Add(new McpAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Test client",
                TokenHash = tokenHash
            });
            await db.SaveChangesAsync();

            return rawToken;
        }
    }
}
