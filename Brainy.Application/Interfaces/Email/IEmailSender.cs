using Brainy.Application.DTOs.Email;

namespace Brainy.Application.Interfaces.Email;

/// <summary>
/// Provider-agnostic outbound email transport, mirroring how <c>IAiAssistant</c> abstracts the
/// AI provider and <c>IBillingProvider</c> abstracts the payment provider: an
/// <c>Email:Provider</c> config value selects the implementation, and
/// <see cref="Email.NullEmailSender"/> is the safe default when none is configured. A real
/// transport (SMTP today; a vendor HTTP API tomorrow) is a drop-in replacement — implement
/// this interface and select it in <c>DependencyInjection.AddEmail</c>.
/// </summary>
/// <remarks>
/// Unlike the billing provider, "not configured" is not represented as a result flag here:
/// callers (password reset, email confirmation) have no honest degraded mode — either the
/// mail is delivered or the caller must find out it wasn't. <see cref="SendAsync"/> therefore
/// throws on any failure to deliver instead of returning a silent false/no-op result.
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Sends <paramref name="message"/>. Throws if delivery cannot be attempted or fails, so
    /// a missing/misconfigured provider or a transport error always surfaces to the caller
    /// instead of failing silently.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
