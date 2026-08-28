using System.Net;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

public sealed class ProductionSurfaceTests(BrainyWebApplicationFactory factory)
    : IClassFixture<BrainyWebApplicationFactory>
{
    [Fact]
    public async Task HttpLoginRedirectsToHttps()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        using var response = await client.GetAsync("/Account/Login");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.MovedPermanently, HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    [Fact]
    public async Task HttpsLoginIncludesSecurityHeaders()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/Account/Login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("strict-origin-when-cross-origin");
        string.Join(";", response.Headers.GetValues("Content-Security-Policy"))
            .Should().Contain("object-src 'none'");
    }

    [Fact]
    public async Task AnonymousNotFoundDoesNotExposeCaptureAction()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/definitely-not-a-brainy-route");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        content.Should().Contain("Page not found");
        content.Should().NotContain(">Capture<");
    }

    [Fact]
    public async Task ProductionRegistrationIsOpen()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/Account/Register");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().NotContain("Registration is closed");
        content.Should().Contain("Create account");
    }

    [Fact]
    public async Task AnonymousLandingPageRendersMarketingContent()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("second brain");
        content.Should().MatchRegex("href=\"marketing[^\"]*\\.css\"");
    }

    [Fact]
    public async Task AnonymousFeaturesPageRendersStatically()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/features");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Everything a second brain needs");
    }

    [Fact]
    public async Task AnonymousPricingPageRendersStatically()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/pricing");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Most popular");
    }

    [Fact]
    public async Task AnonymousChangelogPageRendersStatically()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/changelog");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Changelog");
        content.Should().Contain("5.12.4");
    }

    [Fact]
    public async Task TodayPageRequiresAuthentication()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/today");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.AbsolutePath.Should().Be("/Account/Login");
        response.Headers.Location?.Query.Should().Contain("ReturnUrl=%2Ftoday");
    }

    [Fact]
    public async Task WeekPageRequiresAuthentication()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/today/week");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.AbsolutePath.Should().Be("/Account/Login");
        response.Headers.Location?.Query.Should().Contain("ReturnUrl=%2Ftoday%2Fweek");
    }

    [Fact]
    public async Task LlmFocusPageRequiresAuthentication()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/llm");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.AbsolutePath.Should().Be("/Account/Login");
        response.Headers.Location?.Query.Should().Contain("ReturnUrl=%2Fllm");
    }

    [Fact]
    public async Task LivenessDoesNotRequireDatabase()
    {
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LegacyPlatformHealthProbeDoesNotRequireDatabase()
    {
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
