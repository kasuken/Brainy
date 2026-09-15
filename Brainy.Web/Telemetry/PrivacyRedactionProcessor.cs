using System.Diagnostics;
using OpenTelemetry;

namespace Brainy.Web.Telemetry;

/// <summary>
/// Trace-pipeline safety net for issue #322's hard privacy constraint: strips a small, fixed
/// list of attribute keys that are known (or reasonably likely, given how each instrumentation
/// library behaves today) to carry raw SQL text, a query string, or an auth credential, from
/// every exported span — regardless of which instrumentation library set them, and regardless
/// of any future change to that library's own defaults. This is defense in depth on top of
/// (not a replacement for) never putting user content into a span/attribute/tag in Brainy's own
/// code; see <see cref="Brainy.Application.Telemetry.BrainyTelemetry"/>.
/// </summary>
/// <remarks>
/// Registered first, before the OTLP exporter, in <see cref="DependencyInjection.AddBrainyTelemetry"/>
/// — <see cref="BaseProcessor{T}"/> pipeline stages run in registration order, so this always
/// redacts before the exporter serializes the span.
/// </remarks>
internal sealed class PrivacyRedactionProcessor : BaseProcessor<Activity>
{
    /// <summary>
    /// Attribute keys that must never leave the process. Covers both the OTel semantic
    /// convention names in use today and their still-referenced predecessors, since the exact
    /// key an instrumentation library emits has changed across OTel semantic-convention
    /// versions.
    /// </summary>
    private static readonly string[] RedactedTagKeys =
    [
        "db.statement", // pre-1.24 EF Core/ADO.NET semantic convention: could hold literal SQL text.
        "db.query.text", // current EF Core/ADO.NET semantic convention name for the same thing.
        "url.query", // could echo back a query-string parameter (e.g. a search term).
        "http.request.header.authorization",
        "http.request.header.cookie",
    ];

    public override void OnEnd(Activity data)
    {
        foreach (var key in RedactedTagKeys)
        {
            if (data.GetTagItem(key) is not null)
                data.SetTag(key, null);
        }
    }
}
