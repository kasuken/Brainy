using AwesomeAssertions;
using Brainy.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace Brainy.E2E.Tests;

/// <summary>
/// Verifies that area card titles on the Areas page (/areas) are fully visible and readable,
/// not prematurely clipped or truncated by horizontal ellipsis into illegible substrings
/// like "Career &amp; Cli..." or "Health &amp; Fit...".
/// </summary>
public sealed class AreaPageTitleTests(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task AreasPage_CardTitles_AreFullyVisibleAndNotEllipsisTruncated()
    {
        await RunAsync(async page =>
        {
            var unique = Guid.NewGuid().ToString("N")[..6];
            var areaNames = new[]
            {
                $"Career & Clients {unique}",
                $"Health & Fitness {unique}",
                $"Learning & Development {unique}",
                $"Home & Family {unique}",
                $"Personal Finance {unique}",
                $"Products & Services {unique}"
            };

            await page.RegisterNewUserAsync(BaseUrl);

            foreach (var name in areaNames)
            {
                await page.CreateAreaAsync(BaseUrl, name);
            }

            await page.GotoAsync($"{BaseUrl}/areas");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            foreach (var name in areaNames)
            {
                var titleLink = page.Locator(".ap-card-title a.ap-card-link", new() { HasText = name }).First;
                await titleLink.WaitForAsync(new() { Timeout = 15_000 });

                // Inner text matches full name
                var text = await titleLink.InnerTextAsync();
                text.Trim().Should().Be(name);

                // Verify the title is not horizontally clipped with ellipsis:
                // With white-space: nowrap and ellipsis overflow, scrollWidth > clientWidth.
                // With wrapping allowed, scrollWidth <= clientWidth.
                var isTruncated = await titleLink.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth");
                isTruncated.Should().BeFalse($"Area title '{name}' should fit without horizontal ellipsis clipping");

                // Verify element has positive dimensions and is visible
                var boundingBox = await titleLink.BoundingBoxAsync();
                boundingBox.Should().NotBeNull();
                boundingBox!.Width.Should().BeGreaterThan(0);
                boundingBox.Height.Should().BeGreaterThan(0);
            }

            // Capture screenshot for visual inspection
            var screenshotPath = @"C:\Users\barto\.gemini\antigravity-cli\brain\dbab929e-0644-4fc1-aa1f-c44911668d08\areas_page_after_fix.png";
            await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
        });
    }
}
