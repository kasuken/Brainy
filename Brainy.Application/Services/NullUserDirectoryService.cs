using Brainy.Application.Interfaces.Identity;

namespace Brainy.Application.Services;

/// <summary>
/// Default <see cref="IUserDirectoryService"/> registration used when the host does not
/// override it with a real Identity-backed implementation (e.g. most Application-layer unit
/// tests, which never wire up ASP.NET Core Identity). Always reports zero registered users so
/// a missing override reads as an obviously-unknown denominator rather than a plausible but
/// wrong one; <c>Brainy.Web</c>'s composition root always overrides this with the real
/// <c>UserDirectoryService</c> at startup.
/// </summary>
internal sealed class NullUserDirectoryService : IUserDirectoryService
{
    public Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
