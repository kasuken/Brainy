using Brainy.Application.Interfaces.Identity;

namespace Brainy.Application.Tests.Fakes;

/// <summary>Plain scoped holder mirroring <c>Brainy.Web.Identity.BackgroundUserContext</c>.</summary>
public sealed class FakeBackgroundUserContextAccessor : IBackgroundUserContextAccessor
{
    public string? UserId { get; set; }
}
