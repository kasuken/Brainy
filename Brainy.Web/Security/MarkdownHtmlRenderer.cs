using System.Net;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Brainy.Web.Security;

/// <summary>Renders note Markdown while allowing only safe link destinations.</summary>
public static class MarkdownHtmlRenderer
{
    private const string BlockedDestination = "#";

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsAllowedDestination(link.Url))
                link.Url = BlockedDestination;
        }

        return Markdown.ToHtml(document, Pipeline);
    }

    private static bool IsAllowedDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return true;

        var canonical = Canonicalize(destination);
        if (canonical is null)
            return false;

        if (canonical.Length == 0)
            return true;

        // Network-path references can silently leave Brainy's origin, and browsers
        // interpret backslashes inconsistently. Neither is needed for note links.
        if (canonical.StartsWith("//", StringComparison.Ordinal) || canonical.Contains('\\'))
            return false;

        // A colon before a path/query/fragment delimiter is a scheme even when URI
        // parsing rejects the value. Detect schemes before absolute URI parsing because
        // Unix treats rooted app paths such as /notes/123 as absolute file: URIs.
        var delimiter = canonical.IndexOfAny(['/', '?', '#']);
        var colon = canonical.IndexOf(':');
        if (colon >= 0 && (delimiter < 0 || colon < delimiter))
        {
            return Uri.TryCreate(canonical, UriKind.Absolute, out var absolute)
                && (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || absolute.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase));
        }

        return Uri.TryCreate(canonical, UriKind.Relative, out _);
    }

    private static string? Canonicalize(string destination)
    {
        var value = destination.Trim();

        // Decode repeatedly because nested encoding is commonly used to disguise
        // dangerous schemes. Three passes cover practical browser-decoding chains.
        for (var i = 0; i < 3; i++)
        {
            var decoded = WebUtility.HtmlDecode(value);
            try
            {
                decoded = Uri.UnescapeDataString(decoded);
            }
            catch (UriFormatException)
            {
                return null;
            }

            if (decoded == value)
                break;

            value = decoded;
        }

        // Browsers ignore embedded ASCII whitespace/control characters while
        // interpreting schemes; remove them for the security decision only.
        return string.Concat(value.Where(character =>
            character > ' ' && character != '\u007f'));
    }
}
