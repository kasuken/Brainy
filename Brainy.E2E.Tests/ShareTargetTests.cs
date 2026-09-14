using Brainy.E2E.Tests.Infrastructure;
using Xunit;

namespace Brainy.E2E.Tests;

/// <summary>
/// Covers the PWA share target (manifest.webmanifest's share_target -&gt; /capture/share) —
/// another flow unit tests structurally cannot reach, since it depends on the OS/browser
/// share-sheet handing a GET request with title/text/url query parameters to a real page.
/// </summary>
public sealed class ShareTargetTests(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task ShareTarget_CapturesSharedTextToInbox()
    {
        await RunAsync(async page =>
        {
            await page.RegisterNewUserAsync(BaseUrl);

            var sharedTitle = $"Shared article {Guid.NewGuid():N}";
            var sharedText = "Worth revisiting before the renewal call.";
            var sharedUrl = "https://example.com/insurance-notes";

            // Simulates exactly what the OS share sheet does per manifest.webmanifest's
            // share_target (method GET, params title/text/url) — navigating here directly
            // with those query parameters, the same request shape a share-sheet send produces.
            await page.GotoAsync(
                $"{BaseUrl}/capture/share?title={Uri.EscapeDataString(sharedTitle)}" +
                $"&text={Uri.EscapeDataString(sharedText)}&url={Uri.EscapeDataString(sharedUrl)}");

            await page.GetByText("Saved to Inbox").WaitForAsync(new() { Timeout = 20_000 });
            await page.GetByText(sharedTitle).WaitForAsync();

            await page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Go to Inbox" }).ClickAsync();
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
            await page.Locator(".inbox-card", new Microsoft.Playwright.PageLocatorOptions { HasText = sharedTitle })
                .WaitForAsync(new() { Timeout = 15_000 });
        });
    }
}
