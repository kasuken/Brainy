using AwesomeAssertions;
using Brainy.Application.DTOs.Email;
using Brainy.Application.Email;
using Brainy.Application.Options;
using Brainy.Application.Tests.Fakes;
using Xunit;

namespace Brainy.Application.Tests.Email;

/// <summary>
/// Covers issue #307's "send failures surface as errors rather than silent no-ops" acceptance
/// criterion for the real SMTP transport: an unreachable/refusing host must raise an
/// <see cref="EmailDeliveryException"/>, never report success or swallow the failure.
/// </summary>
public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_WhenTheSmtpServerRefusesTheConnection_ThrowsEmailDeliveryException()
    {
        // Port 1 on loopback has nothing listening, so the OS refuses the connection almost
        // immediately instead of timing out — keeps this test fast and deterministic.
        var options = new EmailOptions
        {
            Provider = EmailProviderType.Smtp,
            FromName = "Brainy",
            FromAddress = "no-reply@brainy-me.com",
            SmtpHost = "127.0.0.1",
            SmtpPort = 1,
        };
        var sender = new SmtpEmailSender(options, new CapturingLogger<SmtpEmailSender>());
        var message = new EmailMessage("reader@example.com", "Confirm your Brainy email address", "plain", "<p>html</p>");

        var act = () => sender.SendAsync(message, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        await act.Should().ThrowAsync<EmailDeliveryException>();
    }
}
