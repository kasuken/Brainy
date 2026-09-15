using System.Security.Claims;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Identity;
using Brainy.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Brainy.Web.Tests.Identity;

/// <summary>
/// Covers <see cref="CurrentUserService"/>'s three-way resolution order — impersonated
/// background user first, then the Blazor circuit's authentication state, then the plain
/// <see cref="HttpContext"/> principal — introduced for issue #315's push dispatch
/// background service (no request/circuit of its own).
/// </summary>
public class CurrentUserServiceTests
{
    private const string CircuitUserId = "circuit-user";
    private const string HttpContextUserId = "http-context-user";
    private const string ImpersonatedUserId = "impersonated-user";

    [Fact]
    public async Task GetUserIdAsync_WithBackgroundUserSet_ReturnsItWithoutConsultingTheCircuit()
    {
        var sut = new CurrentUserService(
            new ThrowingAuthenticationStateProvider(),
            new NullHttpContextAccessor(),
            new BackgroundUserContext { UserId = ImpersonatedUserId });

        var userId = await sut.GetUserIdAsync();

        userId.Should().Be(ImpersonatedUserId);
    }

    [Fact]
    public async Task GetUserIdAsync_WithBackgroundUserSetInsideARequestScope_Throws()
    {
        // Impersonation is consulted before the authenticated principal, so allowing it inside a
        // request scope would silently serve one user's data under another user's request.
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, HttpContextUserId)],
                IdentityConstants.ApplicationScheme))
        };

        var sut = new CurrentUserService(
            new ThrowingAuthenticationStateProvider(),
            new FixedHttpContextAccessor(httpContext),
            new BackgroundUserContext { UserId = ImpersonatedUserId });

        var act = async () => await sut.GetUserIdAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*impersonation*");
    }

    [Fact]
    public async Task GetUserIdAsync_WithNoBackgroundUser_FallsBackToTheCircuitAuthenticationState()
    {
        var sut = new CurrentUserService(
            new FixedAuthenticationStateProvider(CircuitUserId),
            new NullHttpContextAccessor(),
            new BackgroundUserContext());

        var userId = await sut.GetUserIdAsync();

        userId.Should().Be(CircuitUserId);
    }

    [Fact]
    public async Task GetUserIdAsync_WhenCircuitStateThrows_FallsBackToHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, HttpContextUserId)],
                IdentityConstants.ApplicationScheme))
        };

        var sut = new CurrentUserService(
            new ThrowingAuthenticationStateProvider(),
            new FixedHttpContextAccessor(httpContext),
            new BackgroundUserContext());

        var userId = await sut.GetUserIdAsync();

        userId.Should().Be(HttpContextUserId);
    }

    [Fact]
    public async Task GetRequiredUserIdAsync_WithNoUserAnywhere_Throws()
    {
        var sut = new CurrentUserService(
            new ThrowingAuthenticationStateProvider(),
            new NullHttpContextAccessor(),
            new BackgroundUserContext());

        var act = () => sut.GetRequiredUserIdAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private sealed class FixedAuthenticationStateProvider(string userId) : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            IdentityConstants.ApplicationScheme)));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }

    /// <summary>Mirrors what a real circuit-less call site sees: Blazor throws when there is no active circuit.</summary>
    private sealed class ThrowingAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            throw new InvalidOperationException("No circuit is active.");
    }

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class FixedHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}
