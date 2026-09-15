namespace Brainy.Application.Options;

/// <summary>Configuration options for outbound transactional email delivery.</summary>
public sealed class EmailOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "Email";

    /// <summary>The email provider to use. Defaults to <see cref="EmailProviderType.None"/> (email disabled).</summary>
    public EmailProviderType Provider { get; set; } = EmailProviderType.None;

    /// <summary>Sender display name shown in the recipient's inbox, e.g. "Brainy".</summary>
    public string FromName { get; set; } = "Brainy";

    /// <summary>Sender address. Required when <see cref="Provider"/> is not <see cref="EmailProviderType.None"/>.</summary>
    public string? FromAddress { get; set; }

    /// <summary>SMTP host, e.g. smtp.sendgrid.net or smtp.postmarkapp.com.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>SMTP port. Defaults to 587 (STARTTLS submission).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Whether to negotiate transport security with the SMTP server (STARTTLS on 587/25,
    /// implicit TLS on 465). Almost every real SMTP relay requires this; only disable it for
    /// a local, unencrypted dev relay.
    /// </summary>
    public bool SmtpUseSsl { get; set; } = true;

    /// <summary>SMTP authentication username. Most vendor relays require one (e.g. "apikey" for SendGrid).</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>SMTP authentication password or API key. Comes from configuration/User Secrets only, never committed.</summary>
    public string? SmtpPassword { get; set; }
}

/// <summary>Supported email delivery back-ends.</summary>
public enum EmailProviderType
{
    /// <summary>
    /// No live email integration. Outbound messages are logged instead of sent, so local
    /// development and self-hosting keep working without a mail account configured.
    /// </summary>
    None,

    /// <summary>
    /// Plain SMTP submission via MailKit. Vendor-neutral: SendGrid, Postmark, Resend, Azure
    /// Communication Services and self-hosted mail servers all expose an SMTP relay endpoint.
    /// </summary>
    Smtp,
}
