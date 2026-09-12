using Brainy.Application.Interfaces.Identity;
using Brainy.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Web.Identity;

/// <summary>
/// Real <see cref="IUserDirectoryService"/> implementation backed by
/// <see cref="UserManager{TUser}"/>, overriding the Application layer's zero-count default
/// (see <c>NullUserDirectoryService</c>) so the internal analytics dashboard can compute a
/// true registered-user denominator (issue #324).
/// </summary>
internal sealed class UserDirectoryService(UserManager<ApplicationUser> userManager) : IUserDirectoryService
{
    public Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default) =>
        userManager.Users.CountAsync(cancellationToken);
}
