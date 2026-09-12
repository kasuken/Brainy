using System.Net;
using AwesomeAssertions;
using Brainy.Web.Tests.ProductionSurface;
using Xunit;

namespace Brainy.Web.Tests.Identity;

/// <summary>
/// Smoke-tests that issue #307's new Identity /Account pages (password reset, email
/// confirmation) are actually routed and render, so a missing <c>@page</c> directive or a
/// startup wiring mistake shows up here rather than only at manual QA time.
/// </summary>
public sealed class AccountEmailPagesRenderTests(BrainyWebApplicationFactory factory)
    : IClassFixture<BrainyWebApplicationFactory>
{
    [Theory]
    [InlineData("/Account/ForgotPassword", "Forgot your password?")]
    [InlineData("/Account/ForgotPasswordConfirmation", "Check your email")]
    [InlineData("/Account/ResetPasswordConfirmation", "Password reset")]
    [InlineData("/Account/ResendEmailConfirmation", "Resend confirmation email")]
    [InlineData("/Account/RegisterConfirmation", "Check your email")]
    public async Task Page_RendersSuccessfully(string path, string expectedContent)
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain(expectedContent);
    }

    [Fact]
    public async Task ResetPassword_WithAValidLookingCode_RendersTheForm()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/Account/ResetPassword?code=YWJj");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Reset your password");
    }

    [Fact]
    public async Task ResetPassword_WithNoCode_RedirectsToForgotPassword()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/Account/ResetPassword");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Contain("Account/ForgotPassword");
    }

    [Fact]
    public async Task Login_IncludesAForgotPasswordLink()
    {
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Account/ForgotPassword");
    }
}
