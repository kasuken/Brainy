using System.Security.Claims;
using Brainy.Application.Interfaces.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Brainy.Web.Identity;

/// <summary>
/// Resolves the current user's identity key, preferring the Blazor circuit's authentication
/// state (correct for every Razor component call site, at prerender and throughout a live
/// circuit) and falling back to the plain ASP.NET Core <see cref="HttpContext"/> principal.
/// The fallback exists for issue #302's Offline Lite minimal API endpoints
/// (<c>OfflineEndpoints</c>): those run in an ordinary HTTP request pipeline with no Razor
/// component or circuit DI scope at all, where <see cref="AuthenticationStateProvider"/>
/// throws <see cref="InvalidOperationException"/> rather than returning a result.
/// </summary>
/// <remarks>
/// A third source, checked first, is <see cref="IBackgroundUserContextAccessor"/> (backed by
/// the scoped <see cref="BackgroundUserContext"/>): issue #315's push notification
/// dispatcher runs with no request, circuit, or <see cref="HttpContext"/> at all, so it sets
/// that holder to impersonate one user for the DI scope's lifetime instead of every scoped
/// service needing its own explicit-userId variant.
/// </remarks>
internal sealed class CurrentUserService(
    AuthenticationStateProvider authenticationStateProvider,
    IHttpContextAccessor httpContextAccessor,
    IBackgroundUserContextAccessor backgroundUserContext) : ICurrentUserService
{
    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (backgroundUserContext.UserId is { } impersonatedUserId)
        {
            // Impersonation is checked before the authenticated principal, so it must never be
            // reachable from a request scope: anything that set it there would silently override
            // the signed-in user for every scoped service. The only legitimate caller creates a
            // fresh background scope, which has no HttpContext. Fail loudly rather than serving
            // one user's data under another user's request.
            if (httpContextAccessor.HttpContext is not null)
            {
                throw new InvalidOperationException(
                    "Background user impersonation was set inside an HTTP request scope. It is only valid " +
                    "in a dedicated background scope (see IBackgroundUserContextAccessor).");
            }

            return impersonatedUserId;
        }

        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        catch (InvalidOperationException)
        {
            return httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }

    public async Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return userId ?? throw new UnauthorizedAccessException("No authenticated user is available for the current request.");
    }
}
