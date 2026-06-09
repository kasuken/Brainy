using Brainy.Application.Interfaces.Identity;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="ICurrentUserService"/> that returns a fixed user id,
/// allowing service tests to exercise per-user data scoping without an HTTP context.
/// </summary>
public sealed class FakeCurrentUserService(string userId) : ICurrentUserService
{
    public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(userId);

    public Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(userId);
}
