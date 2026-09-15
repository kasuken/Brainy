using Brainy.Application.Email.Templates;
using Brainy.Application.Interfaces.Email;

namespace Brainy.Application.Email;

/// <summary>
/// Renders the account-lifecycle email templates and hands them to the configured
/// <see cref="IEmailSender"/> transport. This is the seam the Web layer's
/// <c>IEmailSender&lt;ApplicationUser&gt;</c> adapter calls into; it never sees
/// <c>ApplicationUser</c> or HTTP context, only the plain values it needs to render a message.
/// </summary>
internal sealed class AccountEmailService(IEmailSender emailSender) : IAccountEmailService
{
    public Task SendEmailConfirmationAsync(string toAddress, string confirmationLink, CancellationToken cancellationToken = default) =>
        emailSender.SendAsync(AccountEmailTemplates.EmailConfirmation(toAddress, confirmationLink), cancellationToken);

    public Task SendPasswordResetLinkAsync(string toAddress, string resetLink, CancellationToken cancellationToken = default) =>
        emailSender.SendAsync(AccountEmailTemplates.PasswordResetLink(toAddress, resetLink), cancellationToken);

    public Task SendPasswordResetCodeAsync(string toAddress, string resetCode, CancellationToken cancellationToken = default) =>
        emailSender.SendAsync(AccountEmailTemplates.PasswordResetCode(toAddress, resetCode), cancellationToken);
}
