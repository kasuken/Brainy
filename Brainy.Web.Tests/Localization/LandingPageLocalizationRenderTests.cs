using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Brainy.Web.Tests.ProductionSurface;
using Xunit;

namespace Brainy.Web.Tests.Localization;

/// <summary>
/// End-to-end proof of the localization pipeline for the marketing surface (issue #323): an
/// anonymous visitor negotiates their language purely from Accept-Language (there is no signed-in
/// user/preference here), and the SEO title/description negotiate along with the visible copy —
/// see docs/roadmap/release-7/17-localization.md's note about keeping canonical URLs and
/// structured data coherent when marketing copy is localized.
/// </summary>
public sealed class LandingPageLocalizationRenderTests(BrainyWebApplicationFactory factory)
    : IClassFixture<BrainyWebApplicationFactory>
{
    [Fact]
    public async Task LandingPage_WithNoAcceptLanguage_RendersEnglish()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Stop storing.");
        content.Should().NotContain("Smetti di archiviare.");
        // The canonical link and structured-data @type values are unaffected by language.
        content.Should().Contain("<link rel=\"canonical\" href=\"https://localhost/\" />");
        content.Should().Contain("\"@type\":\"Organization\"");
    }

    [Fact]
    public async Task LandingPage_WithItalianAcceptLanguage_RendersItalianIncludingSeoMetadata()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("it-IT"));

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain(HtmlEncoder.Default.Encode("Smetti di archiviare. Inizia a "));
        content.Should().NotContain("Stop storing.");
        // SEO title/description follow the negotiated culture too.
        content.Should().Contain($"<title>{HtmlEncoder.Default.Encode("Brainy — Un secondo cervello pratico per professionisti")}</title>");
        // Canonical URL and structured-data @type values stay coherent regardless of language.
        content.Should().Contain("<link rel=\"canonical\" href=\"https://localhost/\" />");
        content.Should().Contain("\"@type\":\"Organization\"");
        content.Should().Contain("\"@type\":\"WebSite\"");
        content.Should().Contain("\"@type\":\"SoftwareApplication\"");
    }
}
