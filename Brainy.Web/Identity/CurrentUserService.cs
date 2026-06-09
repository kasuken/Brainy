using System.Security.Claims;
using Brainy.Application.Interfaces.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace Brainy.Web.Identity;

/// <summary>
/// Resolves the current user's identity key from the Blazor authentication state.
/// Used by the application layer to scope all principal data to the logged-in user.
/// </summary>
internal sealed class CurrentUserService(AuthenticationStateProvider authenticationStateProvider) : ICurrentUserService
{
    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public async Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return userId ?? throw new UnauthorizedAccessException("No authenticated user is available for the current request.");
    }
}
