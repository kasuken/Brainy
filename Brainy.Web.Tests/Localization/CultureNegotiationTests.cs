using System.Net.Http.Headers;
using AwesomeAssertions;
using Brainy.Web.Tests.ProductionSurface;
using Xunit;

namespace Brainy.Web.Tests.Localization;

/// <summary>
/// Exercises RequestLocalizationMiddleware end to end against the anonymous marketing landing
/// page (issue #323). Covers "switching culture changes the UI language" for the negotiation
/// half of that acceptance criterion; MainLayout's per-user persisted-preference half is
/// covered by <see cref="Services.UserCultureServiceTests"/> plus the due-date/UTC boundary
/// tests, since it needs an authenticated circuit rather than a bare HTTP request.
/// </summary>
public sealed class CultureNegotiationTests(BrainyWebApplicationFactory factory)
    : IClassFixture<BrainyWebApplicationFactory>
{
    [Fact]
    public async Task AnonymousRequest_WithNoAcceptLanguage_DefaultsToEnglish()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("<html lang=\"en\"");
    }

    [Fact]
    public async Task AnonymousRequest_WithItalianAcceptLanguage_NegotiatesItalian()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("it-IT"));

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("<html lang=\"it\"");
    }

    [Fact]
    public async Task AnonymousRequest_WithUnsupportedAcceptLanguage_FallsBackToEnglishWithoutThrowing()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fr-FR"));

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("<html lang=\"en\"");
    }
}
