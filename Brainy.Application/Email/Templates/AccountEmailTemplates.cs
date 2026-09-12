using System.Net;
using Brainy.Application.DTOs.Email;

namespace Brainy.Application.Email.Templates;

/// <summary>
/// Plain-text-plus-HTML templates for the account-lifecycle emails Identity needs
/// (email confirmation, password reset), using the Brainy sender identity from the
/// marketing/auth pages (wordmark, clay accent color).
/// </summary>
/// <remarks>
/// Subjects are fixed strings — never built from user-entered content beyond the address the
/// message is sent to — per the "no user content in subject lines" guardrail. Links are the
/// only caller-supplied value interpolated into the body, and they are HTML-encoded before
/// being placed into the HTML document.
/// </remarks>
internal static class AccountEmailTemplates
{
    private const string AccentColor = "#c0561d";

    public static EmailMessage EmailConfirmation(string toAddress, string confirmationLink)
    {
        const string subject = "Confirm your Brainy email address";
        var plainText =
            $"""
            Welcome to Brainy.

            Confirm your email address to finish setting up your account:
            {confirmationLink}

            If you didn't create a Brainy account, you can safely ignore this message.
            """;

        var html = Layout(
            heading: "Confirm your email",
            bodyHtml:
                "<p>Welcome to Brainy. Confirm your email address to finish setting up your account.</p>",
            buttonText: "Confirm email",
            link: confirmationLink,
            footerHtml: "<p>If you didn't create a Brainy account, you can safely ignore this message.</p>");

        return new EmailMessage(toAddress, subject, plainText, html);
    }

    public static EmailMessage PasswordResetLink(string toAddress, string resetLink)
    {
        const string subject = "Reset your Brainy password";
        var plainText =
            $"""
            We received a request to reset your Brainy password.

            Reset your password:
            {resetLink}

            If you didn't request this, you can safely ignore this message — your password won't change.
            """;

        var html = Layout(
            heading: "Reset your password",
            bodyHtml: "<p>We received a request to reset your Brainy password.</p>",
            buttonText: "Reset password",
            link: resetLink,
            footerHtml:
                "<p>If you didn't request this, you can safely ignore this message — your password won't change.</p>");

        return new EmailMessage(toAddress, subject, plainText, html);
    }

    public static EmailMessage PasswordResetCode(string toAddress, string resetCode)
    {
        const string subject = "Your Brainy password reset code";
        var plainText =
            $"""
            We received a request to reset your Brainy password.

            Your reset code: {resetCode}

            If you didn't request this, you can safely ignore this message — your password won't change.
            """;

        var encodedCode = WebUtility.HtmlEncode(resetCode);
        var html = Layout(
            heading: "Reset your password",
            bodyHtml:
                $"""
                <p>We received a request to reset your Brainy password. Enter this code to continue:</p>
                <p style="font-family:'SFMono-Regular',Consolas,monospace;font-size:28px;font-weight:700;
                    letter-spacing:0.08em;text-align:center;margin:24px 0;color:{AccentColor};">{encodedCode}</p>
                """,
            buttonText: null,
            link: null,
            footerHtml:
                "<p>If you didn't request this, you can safely ignore this message — your password won't change.</p>");

        return new EmailMessage(toAddress, subject, plainText, html);
    }

    private static string Layout(string heading, string bodyHtml, string? buttonText, string? link, string footerHtml)
    {
        var encodedHeading = WebUtility.HtmlEncode(heading);
        var buttonHtml = buttonText is not null && link is not null
            ? $"""
               <p style="text-align:center;margin:28px 0;">
                 <a href="{WebUtility.HtmlEncode(link)}" style="background:{AccentColor};color:#ffffff;
                    text-decoration:none;padding:12px 28px;border-radius:8px;font-weight:600;
                    display:inline-block;">{WebUtility.HtmlEncode(buttonText)}</a>
               </p>
               <p style="font-size:13px;color:#6b6b6b;word-break:break-all;">
                 Or copy this link into your browser: {WebUtility.HtmlEncode(link)}
               </p>
               """
            : string.Empty;

        return $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f6f3ef;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',
                Roboto,Helvetica,Arial,sans-serif;color:#242021;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td align="center" style="padding:32px 16px;">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0"
                        style="max-width:480px;width:100%;background:#ffffff;border-radius:12px;
                        padding:32px;box-sizing:border-box;">
                      <tr>
                        <td>
                          <p style="font-weight:700;font-size:20px;color:{AccentColor};margin:0 0 24px;">Brainy</p>
                          <h1 style="font-size:20px;margin:0 0 16px;">{encodedHeading}</h1>
                          {bodyHtml}
                          {buttonHtml}
                          <hr style="border:none;border-top:1px solid #ececec;margin:24px 0;" />
                          <div style="font-size:13px;color:#6b6b6b;">
                            {footerHtml}
                          </div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
