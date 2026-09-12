namespace Brainy.Application.Interfaces.Identity;

/// <summary>
/// Read-only aggregate count over the ASP.NET Core Identity user store, exposed to the
/// Application layer so cross-user reporting (the internal analytics dashboard, issue #324)
/// can compute a true "registered users" denominator without giving Application direct
/// access to Identity infrastructure. Implemented by the host (Web) using
/// <c>UserManager&lt;ApplicationUser&gt;</c>; see <see cref="ICurrentUserService"/> for the
/// equivalent per-request identity seam.
/// </summary>
public interface IUserDirectoryService
{
    /// <summary>
    /// Total number of registered Identity users, regardless of analytics consent. Used only
    /// as an aggregate count — never to enumerate or identify individual users.
    /// </summary>
    Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default);
}
