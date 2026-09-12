namespace Brainy.Application.Interfaces.Email;

/// <summary>
/// Templated account-lifecycle email (email confirmation, password reset) for ASP.NET Core
/// Identity. The Web layer's <c>IEmailSender&lt;ApplicationUser&gt;</c> adapter builds the
/// callback link/code from <c>UserManager</c> and <c>NavigationManager</c> (both unavailable
/// here) and delegates the templating and transport to this service, keeping Identity/HTTP
/// concerns out of the Application layer.
/// </summary>
public interface IAccountEmailService
{
    /// <summary>Sends the "confirm your email" message containing <paramref name="confirmationLink"/>.</summary>
    Task SendEmailConfirmationAsync(string toAddress, string confirmationLink, CancellationToken cancellationToken = default);

    /// <summary>Sends the "reset your password" message containing <paramref name="resetLink"/>.</summary>
    Task SendPasswordResetLinkAsync(string toAddress, string resetLink, CancellationToken cancellationToken = default);

    /// <summary>Sends the "reset your password" message containing a short-lived <paramref name="resetCode"/>.</summary>
    Task SendPasswordResetCodeAsync(string toAddress, string resetCode, CancellationToken cancellationToken = default);
}
