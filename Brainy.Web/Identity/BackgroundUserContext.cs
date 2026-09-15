using Brainy.Application.Interfaces.Identity;

namespace Brainy.Web.Identity;

/// <summary>
/// Scoped implementation of <see cref="IBackgroundUserContextAccessor"/>. Registered as
/// Scoped so each DI scope (per user, per background job iteration) gets its own instance —
/// see <see cref="CurrentUserService"/> for how it is consulted, and
/// <c>Brainy.Application.Services.PushDispatchService</c> for the only current caller.
/// </summary>
internal sealed class BackgroundUserContext : IBackgroundUserContextAccessor
{
    public string? UserId { get; set; }
}
