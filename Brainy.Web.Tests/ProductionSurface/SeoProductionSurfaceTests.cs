using System.Net;
using System.Xml.Linq;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

public sealed class SeoProductionSurfaceTests(BrainyWebApplicationFactory factory)
    : IClassFixture<BrainyWebApplicationFactory>
{
    public static TheoryData<string, string, string> MarketingPages => new()
    {
        { "/", "Brainy — A practical second brain for professionals", "Brainy turns scattered information into organized, reusable knowledge." },
        { "/features", "Features — Brainy", "Everything a second brain needs" },
        { "/pricing", "Pricing — Brainy", "Simple pricing for a practical second brain" },
        { "/changelog", "Changelog — Brainy", "See what is new in Brainy" }
    };

    [Theory]
    [MemberData(nameof(MarketingPages))]
    public async Task MarketingPages_ExposeIndexableCanonicalAndSocialMetadata(
        string path,
        string title,
        string descriptionFragment)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain($"<title>{title}</title>");
        content.Should().Contain(descriptionFragment);
        content.Should().Contain($"<link rel=\"canonical\" href=\"https://localhost{path}\" />");
        content.Should().Contain("<meta property=\"og:title\"");
        content.Should().Contain("<meta property=\"og:description\"");
        content.Should().Contain("<meta property=\"og:url\"");
        content.Should().Contain("<meta property=\"og:image\" content=\"https://localhost/img/og-preview.png\"");
        content.Should().Contain("<meta name=\"twitter:card\" content=\"summary_large_image\"");
        content.Should().NotContain("name=\"robots\" content=\"noindex, nofollow\"");
    }

    [Fact]
    public async Task LandingPage_ContainsExpectedStructuredDataTypes()
    {
        using var client = CreateClient();
        var content = await client.GetStringAsync("/");

        content.Should().Contain("<script type=\"application/ld+json\">");
        content.Should().Contain("\"@context\":\"https://schema.org\"");
        content.Should().Contain("\"@type\":\"Organization\"");
        content.Should().Contain("\"@type\":\"WebSite\"");
        content.Should().Contain("\"@type\":\"SoftwareApplication\"");
    }

    [Fact]
    public async Task PricingPage_ContainsProductOffersStructuredData()
    {
        using var client = CreateClient();
        var content = await client.GetStringAsync("/pricing");

        content.Should().Contain("\"@type\":\"Product\"");
        content.Should().Contain("\"@type\":\"Offer\"");
        content.Should().Contain("\"name\":\"Starter\"");
        content.Should().Contain("\"name\":\"Pro\"");
    }

    [Fact]
    public async Task ProductionRobotsTxt_AllowsMarketingAndReferencesSitemap()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/robots.txt");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Contain("User-agent: *");
        content.Should().Contain("Allow: /");
        content.Should().Contain("Disallow: /Account");
        content.Should().Contain("Disallow: /today");
        content.Should().Contain("Sitemap: https://localhost/sitemap.xml");
    }

    [Fact]
    public async Task ProductionSitemap_ContainsExactlyThePublicMarketingPages()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/sitemap.xml");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");

        var document = XDocument.Parse(content);
        XNamespace sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = document.Root!
            .Elements(sitemap + "url")
            .Select(element => element.Element(sitemap + "loc")!.Value)
            .ToArray();

        locations.Should().Equal(
            "https://localhost/",
            "https://localhost/features",
            "https://localhost/pricing",
            "https://localhost/changelog");
        content.Should().NotContain("/Account");
        content.Should().NotContain("/today");
    }

    [Fact]
    public async Task SocialPreviewImage_IsServed()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/img/og-preview.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/not-found")]
    [InlineData("/Error")]
    public async Task PrivateAndErrorPages_AreNoIndex(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("<meta name=\"robots\" content=\"noindex, nofollow\"");
    }

    private HttpClient CreateClient() => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });
}

public sealed class SeoDevelopmentSurfaceTests(DevelopmentBrainyWebApplicationFactory factory)
    : IClassFixture<DevelopmentBrainyWebApplicationFactory>
{
    [Fact]
    public async Task NonProductionRobotsTxt_DisallowsCrawling()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/robots.txt");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("User-agent: *");
        content.Should().Contain("Disallow: /");
        content.Should().NotContain("Sitemap:");
    }
}
