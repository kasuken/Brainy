namespace Brainy.Application.Interfaces.Identity;

/// <summary>
/// Provides the identity of the currently authenticated user to the application layer.
/// Implemented by the host (Web) using the active authentication state.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Returns the current user's identity key, or null if unauthenticated.</summary>
    Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current user's identity key, throwing
    /// <see cref="UnauthorizedAccessException"/> when no user is authenticated.
    /// </summary>
    Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default);
}
