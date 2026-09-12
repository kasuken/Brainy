using Brainy.Application.DTOs.Email;
using Brainy.Application.Interfaces.Email;
using Brainy.Application.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Brainy.Application.Email;

/// <summary>
/// <see cref="IEmailSender"/> transport that submits mail over SMTP via MailKit. Deliberately
/// vendor-neutral: SendGrid, Postmark, Resend, Azure Communication Services and a plain
/// self-hosted mail server all expose an SMTP relay endpoint, so one implementation covers
/// every provider — only <see cref="EmailOptions"/> changes between them.
/// </summary>
/// <remarks>
/// Never swallows a delivery failure: a connection, authentication or send error from MailKit
/// is wrapped in an <see cref="EmailDeliveryException"/> and rethrown, so callers see a visible
/// error instead of a silently-dropped message.
/// </remarks>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly string _fromName;
    private readonly string _fromAddress;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly bool _smtpUseSsl;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>
    /// <paramref name="options"/> is validated by <c>DependencyInjection.AddEmail</c> before
    /// this type is ever constructed: <see cref="EmailOptions.SmtpHost"/> and
    /// <see cref="EmailOptions.FromAddress"/> are required to be non-blank there.
    /// </summary>
    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _fromName = options.FromName;
        _fromAddress = options.FromAddress!;
        _smtpHost = options.SmtpHost!;
        _smtpPort = options.SmtpPort;
        _smtpUseSsl = options.SmtpUseSsl;
        _smtpUsername = options.SmtpUsername;
        _smtpPassword = options.SmtpPassword;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_fromName, _fromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.ToAddress));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new BodyBuilder
        {
            TextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _smtpUseSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.None;

            await client.ConnectAsync(_smtpHost, _smtpPort, socketOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_smtpUsername))
            {
                await client.AuthenticateAsync(_smtpUsername, _smtpPassword ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }

            await client.SendAsync(mimeMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send email {Subject} to {ToAddress} via SMTP host {SmtpHost}.",
                message.Subject, message.ToAddress, _smtpHost);
            throw new EmailDeliveryException(
                $"Failed to send email to {message.ToAddress} via SMTP host {_smtpHost}.", ex);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
