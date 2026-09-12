using Brainy.Application.Interfaces.Email;
using Brainy.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace Brainy.Web.Identity;

/// <summary>
/// Adapts ASP.NET Core Identity's <see cref="IEmailSender{TUser}"/> callback shape (called by
/// the Identity /Account Razor components for registration confirmation and password reset)
/// onto <see cref="IAccountEmailService"/>. This is the only place <c>ApplicationUser</c> and
/// the Identity email contract meet the Application-layer email abstraction; templating and
/// transport selection stay in <c>Brainy.Application</c> so they can be unit-tested without a
/// Blazor circuit or database.
/// </summary>
public sealed class BrainyIdentityEmailSender(IAccountEmailService accountEmailService)
    : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        accountEmailService.SendEmailConfirmationAsync(email, confirmationLink);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        accountEmailService.SendPasswordResetLinkAsync(email, resetLink);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        accountEmailService.SendPasswordResetCodeAsync(email, resetCode);
}
