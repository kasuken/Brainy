using System.Text;
using System.Xml.Linq;
using Brainy.Web.Configuration;

namespace Brainy.Web.Endpoints;

/// <summary>Maps crawler-facing SEO endpoints for the public marketing site.</summary>
public static class SeoEndpoints
{
    private static readonly IReadOnlyList<SitemapEntry> PublicPages =
    [
        new("/", "weekly", "1.0"),
        new("/features", "monthly", "0.8"),
        new("/pricing", "monthly", "0.8"),
        new("/changelog", "weekly", "0.6"),
        new("/privacy", "yearly", "0.3"),
        new("/terms", "yearly", "0.3"),
        new("/acceptable-use", "yearly", "0.3"),
        new("/ai-transparency", "monthly", "0.5")
    ];

    /// <summary>Maps the robots and sitemap endpoints.</summary>
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/robots.txt", (IHostEnvironment environment, SeoOptions options) =>
        {
            var content = environment.IsProduction()
                ? BuildProductionRobots(options)
                : "User-agent: *\nDisallow: /\n";

            return Results.Text(content, "text/plain", Encoding.UTF8);
        });

        endpoints.MapGet("/sitemap.xml", (IHostEnvironment environment, SeoOptions options) =>
        {
            if (!environment.IsProduction())
                return Results.NotFound();

            XNamespace sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(sitemap + "urlset",
                    PublicPages.Select(page =>
                        new XElement(sitemap + "url",
                            new XElement(sitemap + "loc", BuildAbsoluteUrl(options, page.Path)),
                            new XElement(sitemap + "changefreq", page.ChangeFrequency),
                            new XElement(sitemap + "priority", page.Priority)))));

            return Results.Text(document.ToString(SaveOptions.DisableFormatting), "application/xml", Encoding.UTF8);
        });

        return endpoints;
    }

    private static string BuildProductionRobots(SeoOptions options) => string.Join('\n',
    [
        "User-agent: *",
        "Allow: /",
        "Disallow: /Account",
        "Disallow: /today",
        "Disallow: /inbox",
        "Disallow: /projects",
        "Disallow: /areas",
        "Disallow: /resources",
        "Disallow: /archives",
        "Disallow: /search",
        "Disallow: /outputs",
        "Disallow: /tasks",
        "Disallow: /goals",
        "Disallow: /ideas",
        "Disallow: /pulse",
        "Disallow: /week",
        "Disallow: /llm",
        $"Sitemap: {BuildAbsoluteUrl(options, "/sitemap.xml")}",
        string.Empty
    ]);

    private static string BuildAbsoluteUrl(SeoOptions options, string path)
        => $"{options.SiteOrigin.TrimEnd('/')}/{path.TrimStart('/')}";

    private sealed record SitemapEntry(string Path, string ChangeFrequency, string Priority);
}
