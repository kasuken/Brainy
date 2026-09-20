using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Application;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Web.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Brainy.Web.Tests.Mcp;

/// <summary>
/// Covers the MCP bearer authentication scheme (Phase 1, Step 2). The token is the entire
/// credential, so these tests drive <see cref="McpAuthenticationHandler"/> against a real
/// <see cref="IMcpAccessTokenService"/> over an in-memory database, seeding token rows exactly
/// as the service hashes them. The security gate: a token resolves to <b>only</b> its owner,
/// a revoked or unknown token fails, and a missing credential is anonymous (a clean 401).
/// The end-to-end test through the mapped <c>/api/mcp</c> endpoint follows in Step 3.
/// </summary>
public sealed class McpAuthenticationHandlerTests
{
    private const string UserA = "mcp-auth-user-a";
    private const string UserB = "mcp-auth-user-b";
    private const string TokenPrefix = "brainy_mcp_";

    [Fact]
    public async Task Authenticate_WithNoAuthorizationHeader_ReturnsNoResult()
    {
        await using var provider = BuildProvider(nameof(Authenticate_WithNoAuthorizationHeader_ReturnsNoResult));

        var result = await AuthenticateAsync(provider, authorizationHeader: null);

        result.None.Should().BeTrue("a missing credential is anonymous, not a hard failure");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_WithNonBearerHeader_ReturnsNoResult()
    {
        await using var provider = BuildProvider(nameof(Authenticate_WithNonBearerHeader_ReturnsNoResult));

        var result = await AuthenticateAsync(provider, "Basic dXNlcjpwYXNz");

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task Authenticate_WithValidToken_SucceedsAsThatUser()
    {
        await using var provider = BuildProvider(nameof(Authenticate_WithValidToken_SucceedsAsThatUser));
        var token = await SeedTokenAsync(provider, UserA, "Claude Desktop");

        var result = await AuthenticateAsync(provider, $"Bearer {token}");

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(UserA,
            "the resolved user id must be the NameIdentifier claim CurrentUserService reads");
    }

    [Fact]
    public async Task Authenticate_ResolvesEachTokenToItsOwnUser_NeverAnother()
    {
        await using var provider = BuildProvider(nameof(Authenticate_ResolvesEachTokenToItsOwnUser_NeverAnother));
        var tokenA = await SeedTokenAsync(provider, UserA, "A's client");
        var tokenB = await SeedTokenAsync(provider, UserB, "B's client");

        var resultA = await AuthenticateAsync(provider, $"Bearer {tokenA}");
        var resultB = await AuthenticateAsync(provider, $"Bearer {tokenB}");

        resultA.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(UserA);
        resultB.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(UserB,
            "a token authenticates as exactly its owner regardless of any other user's tokens");
    }

    [Fact]
    public async Task Authenticate_WithUnknownToken_Fails()
    {
        await using var provider = BuildProvider(nameof(Authenticate_WithUnknownToken_Fails));
        var wellFormedButUnknown = TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var result = await AuthenticateAsync(provider, $"Bearer {wellFormedButUnknown}");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Authenticate_WithMalformedToken_Fails()
    {
        await using var provider = BuildProvider(nameof(Authenticate_WithMalformedToken_Fails));

        var result = await AuthenticateAsync(provider, "Bearer not-a-real-token");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_AfterRevoke_Fails()
    {
        await using var provider = BuildProvider(nameof(Authenticate_AfterRevoke_Fails));
        var token = await SeedTokenAsync(provider, UserA, "Client");

        var beforeRevoke = await AuthenticateAsync(provider, $"Bearer {token}");
        await RevokeAllAsync(provider, UserA);
        var afterRevoke = await AuthenticateAsync(provider, $"Bearer {token}");

        beforeRevoke.Succeeded.Should().BeTrue();
        afterRevoke.Succeeded.Should().BeFalse("revoking must break authentication on the very next request");
    }

    private static ServiceProvider BuildProvider(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        // The token service constructor requires a current-user accessor for its issue/list/
        // revoke paths; ResolveUserIdAsync (the only path the handler uses) never consults it.
        services.AddScoped<ICurrentUserService, UnusedCurrentUserService>();
        services.AddBrainyApplication();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Inserts one active token row for <paramref name="userId"/>, hashing the raw token exactly
    /// as <c>McpAccessTokenService</c> does, and returns the raw token to present as a bearer.
    /// </summary>
    private static async Task<string> SeedTokenAsync(IServiceProvider provider, string userId, string name)
    {
        var rawToken = TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        db.McpAccessTokens.Add(new McpAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = tokenHash
        });
        await db.SaveChangesAsync();

        return rawToken;
    }

    private static async Task RevokeAllAsync(IServiceProvider provider, string userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var tokens = await db.McpAccessTokens.Where(t => t.UserId == userId).ToListAsync();
        foreach (var token in tokens)
            token.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        IServiceProvider provider,
        string? authorizationHeader)
    {
        // A fresh request scope per call, exactly like a real request: the handler resolves the
        // token service from HttpContext.RequestServices.
        var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (authorizationHeader is not null)
            httpContext.Request.Headers.Authorization = authorizationHeader;

        var handler = new McpAuthenticationHandler(
            new StaticOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var scheme = new AuthenticationScheme(
            McpAuthenticationDefaults.AuthenticationScheme,
            displayName: null,
            typeof(McpAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private sealed class UnusedCurrentUserService : ICurrentUserService
    {
        public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The MCP authentication handler must not resolve the current user.");

        public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The MCP authentication handler must not resolve the current user.");
    }
}
