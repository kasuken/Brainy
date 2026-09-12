using AwesomeAssertions;
using Brainy.Application.DTOs.Email;
using Brainy.Application.Email;
using Brainy.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Brainy.Application.Tests.Email;

/// <summary>
/// Covers issue #307's "Email:Provider=None starts the app and logs outbound messages
/// without attempting delivery" acceptance criterion.
/// </summary>
public sealed class NullEmailSenderTests
{
    [Fact]
    public async Task SendAsync_CompletesSuccessfully_WithoutAttemptingDelivery()
    {
        var logger = new CapturingLogger<NullEmailSender>();
        var sender = new NullEmailSender(logger);
        var message = new EmailMessage("user@example.com", "Confirm your Brainy email address", "plain", "<p>html</p>");

        var act = () => sender.SendAsync(message);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_LogsTheOutboundMessageInsteadOfSendingIt()
    {
        var logger = new CapturingLogger<NullEmailSender>();
        var sender = new NullEmailSender(logger);
        var message = new EmailMessage("someone@example.com", "Reset your Brainy password", "reset link here", "<p>reset link here</p>");

        await sender.SendAsync(message);

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(message.ToAddress);
        entry.Message.Should().Contain(message.Subject);
    }
}
