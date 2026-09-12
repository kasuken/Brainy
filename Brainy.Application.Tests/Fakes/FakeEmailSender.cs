using Brainy.Application.DTOs.Email;
using Brainy.Application.Interfaces.Email;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IEmailSender"/> that records every message it is asked to send,
/// and can be configured to throw instead — so tests can assert both what a template rendered
/// and that a transport failure propagates rather than being swallowed.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    private readonly Exception? _throwOnSend;

    public FakeEmailSender(Exception? throwOnSend = null)
    {
        _throwOnSend = throwOnSend;
    }

    public List<EmailMessage> SentMessages { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (_throwOnSend is not null)
        {
            throw _throwOnSend;
        }

        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
