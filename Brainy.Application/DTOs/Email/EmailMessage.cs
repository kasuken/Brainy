namespace Brainy.Application.DTOs.Email;

/// <summary>
/// A rendered outbound email, ready to hand to an <see cref="Interfaces.Email.IEmailSender"/>
/// provider. Always carries both a plain-text and an HTML body so it renders correctly
/// regardless of the recipient's mail client.
/// </summary>
/// <param name="ToAddress">The recipient's email address.</param>
/// <param name="Subject">
/// The message subject. Templates keep this static (no user-entered content beyond what the
/// user themselves typed, e.g. their own email address) so outbound mail cannot be used to
/// inject attacker-controlled text into a subject line.
/// </param>
/// <param name="PlainTextBody">Plain-text rendering of the message body.</param>
/// <param name="HtmlBody">HTML rendering of the message body.</param>
public sealed record EmailMessage(string ToAddress, string Subject, string PlainTextBody, string HtmlBody);
