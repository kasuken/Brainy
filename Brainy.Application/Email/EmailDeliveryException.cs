namespace Brainy.Application.Email;

/// <summary>
/// Thrown when a configured <see cref="Interfaces.Email.IEmailSender"/> fails to deliver a
/// message. Callers (password reset, email confirmation) must let this propagate as a visible
/// error rather than catching it and reporting success — a missing or misconfigured provider,
/// or a transport failure, must never look like a silently-dropped email.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
