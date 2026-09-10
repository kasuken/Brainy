using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.Endpoints;

/// <summary>
/// Inbound billing-provider webhook endpoint. All verification and state-application logic
/// lives in <see cref="IBillingWebhookProcessor"/> (Application layer); this endpoint only
/// reads the request and maps the result to an HTTP status.
/// </summary>
public static class BillingWebhookEndpoints
{
    /// <summary>The header a billing provider signs its webhook payload with.</summary>
    public const string SignatureHeaderName = "X-Billing-Signature";

    public static IEndpointRouteBuilder MapBillingWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/webhooks/billing", async (
            HttpRequest request,
            IBillingWebhookProcessor processor,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            var signature = request.Headers[SignatureHeaderName].ToString();

            var result = await processor.ProcessAsync(payload, signature, cancellationToken);

            // Every accepted outcome (applied, already-processed, or an event type we don't
            // act on) returns 200 so the provider stops retrying. Only a failed signature
            // check is rejected.
            return result.Accepted ? Results.Ok() : Results.Unauthorized();
        })
        // Called by the billing provider, not an authenticated Brainy user or browser form.
        .DisableAntiforgery()
        .AllowAnonymous();

        return endpoints;
    }
}
