using System.Security.Claims;
using System.Text.Encodings.Web;
using Brainy.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Brainy.Web.Mcp;

/// <summary>
/// Authenticates a Model Context Protocol request by the bearer token in its
/// <c>Authorization</c> header, resolving it to the owning Brainy user through
/// <see cref="IMcpAccessTokenService.ResolveUserIdAsync"/>. On success it issues a principal
/// carrying only the user's identity key as <see cref="ClaimTypes.NameIdentifier"/> — the very
/// claim <c>CurrentUserService</c> reads from <c>HttpContext.User</c> when there is no Blazor
/// circuit — so every downstream application service scopes to that user with no further
/// wiring. No cookie, circuit, or server session is involved: the token is the entire
/// credential, the same model the read-only ICS calendar feed uses.
/// </summary>
internal sealed class McpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No bearer credential presented: NoResult (not Fail) so the request is simply
        // treated as anonymous. The authorization policy then issues a clean 401 challenge,
        // indistinguishable from the response to a wrong token — an unauthenticated caller
        // should not be able to tell "no credential" apart from "bad credential".
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            return AuthenticateResult.NoResult();

        var raw = authorizationHeader.ToString();
        if (string.IsNullOrEmpty(raw) ||
            !raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = raw[BearerPrefix.Length..].Trim();

        // Resolved from the request scope, not injected, so it shares the request's scoped
        // DbContext with everything else on this request.
        var tokenService = Context.RequestServices.GetRequiredService<IMcpAccessTokenService>();
        var userId = await tokenService
            .ResolveUserIdAsync(token, Context.RequestAborted)
            .ConfigureAwait(false);

        if (userId is null)
            return AuthenticateResult.Fail("The MCP access token is invalid or has been revoked.");

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // A bare "Bearer" challenge: enough for a client to know a token is required, without
        // leaking whether the endpoint, a realm, or a specific scope was the problem.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
