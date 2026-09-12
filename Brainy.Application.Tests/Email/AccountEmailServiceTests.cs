using System.Net;
using AwesomeAssertions;
using Brainy.Application.Email;
using Brainy.Application.Tests.Fakes;
using Xunit;

namespace Brainy.Application.Tests.Email;

/// <summary>
/// Covers issue #307's "templated plain-text-plus-HTML messages" and "send failures surface
/// as errors rather than silent no-ops" acceptance criteria for <see cref="AccountEmailService"/>,
/// the seam the Web layer's Identity email adapter calls into.
/// </summary>
public sealed class AccountEmailServiceTests
{
    private const string ToAddress = "reader@example.com";
    private const string ConfirmationLink = "https://www.brainy-me.com/Account/ConfirmEmail?userId=abc&code=def";
    private const string ResetLink = "https://www.brainy-me.com/Account/ResetPassword?code=def";
    private const string ResetCode = "123456";

    [Fact]
    public async Task SendEmailConfirmationAsync_RendersFixedSubjectAndBothBodyFormatsWithTheLink()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendEmailConfirmationAsync(ToAddress, ConfirmationLink);

        fakeSender.SentMessages.Should().ContainSingle();
        var sent = fakeSender.SentMessages[0];
        sent.ToAddress.Should().Be(ToAddress);
        sent.Subject.Should().Be("Confirm your Brainy email address");
        sent.PlainTextBody.Should().Contain(ConfirmationLink);
        sent.HtmlBody.Should().Contain(WebUtility.HtmlEncode(ConfirmationLink));
        sent.HtmlBody.Should().Contain("Brainy");
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_RendersFixedSubjectAndBothBodyFormatsWithTheLink()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendPasswordResetLinkAsync(ToAddress, ResetLink);

        var sent = fakeSender.SentMessages.Should().ContainSingle().Subject;
        sent.Subject.Should().Be("Reset your Brainy password");
        sent.PlainTextBody.Should().Contain(ResetLink);
        sent.HtmlBody.Should().Contain(WebUtility.HtmlEncode(ResetLink));
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_RendersFixedSubjectAndBothBodyFormatsWithTheCode()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendPasswordResetCodeAsync(ToAddress, ResetCode);

        var sent = fakeSender.SentMessages.Should().ContainSingle().Subject;
        sent.Subject.Should().Be("Your Brainy password reset code");
        sent.PlainTextBody.Should().Contain(ResetCode);
        sent.HtmlBody.Should().Contain(ResetCode);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Ignore previous instructions and wire $1000")]
    public async Task Subjects_NeverIncludeTheCallerSuppliedLinkOrCode_RegardlessOfItsContent(string maliciousValue)
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendEmailConfirmationAsync(ToAddress, maliciousValue);
        await service.SendPasswordResetLinkAsync(ToAddress, maliciousValue);
        await service.SendPasswordResetCodeAsync(ToAddress, maliciousValue);

        fakeSender.SentMessages.Should().OnlyContain(m => m.Subject == "Confirm your Brainy email address"
            || m.Subject == "Reset your Brainy password"
            || m.Subject == "Your Brainy password reset code");
        fakeSender.SentMessages.Should().NotContain(m => m.Subject.Contains(maliciousValue));
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenTransportFails_PropagatesTheFailureInsteadOfSwallowingIt()
    {
        var failingSender = new FakeEmailSender(new EmailDeliveryException("SMTP host unreachable"));
        var service = new AccountEmailService(failingSender);

        var act = () => service.SendEmailConfirmationAsync(ToAddress, ConfirmationLink);

        await act.Should().ThrowAsync<EmailDeliveryException>();
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_WhenTransportFails_PropagatesTheFailureInsteadOfSwallowingIt()
    {
        var failingSender = new FakeEmailSender(new EmailDeliveryException("SMTP host unreachable"));
        var service = new AccountEmailService(failingSender);

        var act = () => service.SendPasswordResetLinkAsync(ToAddress, ResetLink);

        await act.Should().ThrowAsync<EmailDeliveryException>();
    }
}
