using Brainy.Application.DTOs.Email;
using Brainy.Application.Interfaces.Email;
using Microsoft.Extensions.Logging;

namespace Brainy.Application.Email;

/// <summary>
/// Safe no-op <see cref="IEmailSender"/> used when <c>Email:Provider</c> is <c>None</c> (the
/// default). Logs the outbound message instead of attempting delivery, so local development
/// and self-hosting keep working without a mail account configured — mirroring
/// <see cref="Billing.NullBillingProvider"/> and <see cref="AI.NullAiAssistant"/>.
/// </summary>
/// <remarks>
/// This is a deliberate, visible stand-in, not a silent drop: every call is logged at
/// <see cref="LogLevel.Warning"/> with the recipient and subject, and the completed task
/// never reports success by accident — callers relying on delivery in this mode will see it
/// in logs. A real transport is a drop-in replacement: implement <see cref="IEmailSender"/>
/// and select it in <c>DependencyInjection.AddEmail</c> based on <c>EmailOptions.Provider</c>.
/// </remarks>
internal sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Email:Provider is None; not sending mail. Would have sent \"{Subject}\" to {ToAddress}.\n{PlainTextBody}",
            message.Subject, message.ToAddress, message.PlainTextBody);
        return Task.CompletedTask;
    }
}
