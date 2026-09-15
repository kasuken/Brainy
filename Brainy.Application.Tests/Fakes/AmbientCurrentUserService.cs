using Brainy.Application.Interfaces.Identity;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Test double mirroring <c>Brainy.Web.Identity.CurrentUserService</c>'s impersonation
/// behavior: resolves the current user id from a scoped <see cref="IBackgroundUserContextAccessor"/>
/// instead of a fixed value, so tests can exercise <c>PushDispatchService</c> evaluating
/// multiple users, each in its own DI scope, exactly as production does.
/// </summary>
public sealed class AmbientCurrentUserService(IBackgroundUserContextAccessor backgroundUserContext) : ICurrentUserService
{
    public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(backgroundUserContext.UserId);

    public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(backgroundUserContext.UserId
            ?? throw new UnauthorizedAccessException("No impersonated user set for this scope."));
}
