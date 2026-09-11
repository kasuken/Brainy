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
internal sealed class CurrentUserService(
    AuthenticationStateProvider authenticationStateProvider,
    IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
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
