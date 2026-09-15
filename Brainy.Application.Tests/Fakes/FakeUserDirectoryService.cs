using Brainy.Application.Interfaces.Identity;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IUserDirectoryService"/> that returns a fixed registered-user
/// count, so tests can exercise the activation-funnel's consent-gap reporting without a real
/// ASP.NET Core Identity store.
/// </summary>
public sealed class FakeUserDirectoryService(int registeredUserCount) : IUserDirectoryService
{
    public Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(registeredUserCount);
}
