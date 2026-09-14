using Brainy.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Brainy.E2E.Tests;

/// <summary>
/// Covers offline capture and reconnect sync (Offline Lite, issue #302) — one of the flows
/// unit tests structurally cannot reach, since it depends on real browser network state and
/// IndexedDB (see wwwroot/offline.html and offlineCapture.js).
/// </summary>
public sealed class OfflineCaptureTests(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task OfflineCapture_SyncsAutomaticallyOnceReconnected()
    {
        await RunAsync(async page =>
        {
            // The static offline fallback page (wwwroot/offline.html) is cookie-authenticated
            // like the rest of the app (see OfflineEndpoints.cs) but has no Blazor circuit at
            // all — it must work precisely when the browser has no connectivity, which is
            // also why the test drives it directly rather than via a service-worker
            // navigation fallback.
            await page.RegisterNewUserAsync(BaseUrl);

            var captureText = $"Offline capture {Guid.NewGuid():N}";

            await page.GotoAsync($"{BaseUrl}/offline.html");
            var captureTextArea = page.Locator("#capture-text");
            await captureTextArea.WaitForAsync();

            // A real network condition (Chromium's offline emulation), not a flag our own
            // code invents — the same mechanism a real lost connection would trigger.
            await page.Context.SetOfflineAsync(true);

            await captureTextArea.FillAsync(captureText);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save on this device" }).ClickAsync();

            var queueItem = page.Locator(".queue-item", new PageLocatorOptions { HasText = captureText });
            await Expect(queueItem.Locator(".status-badge")).ToHaveTextAsync("Queued", new() { Timeout = 10_000 });

            await page.Context.SetOfflineAsync(false);

            // offlineCapture.js's own foreground trigger for "connectivity is back": the
            // browser's 'online' event, whose handler calls exactly this same function.
            // Headless Chromium's offline emulation toggles the network layer without
            // reliably dispatching that DOM event, so this test calls the same exported
            // function the event handler would — the real sync codepath, just invoked
            // directly instead of depending on an event Chromium may not fire.
            await page.EvaluateAsync("() => window.offlineCapture.flushQueue()");

            // The on-page queue list is a one-time render, not reactive to the IndexedDB
            // change above, so poll the module's own snapshot API — the same one the UI
            // itself reads from — rather than the (unrefreshed) DOM.
            await page.WaitForFunctionAsync(
                """
                async (text) => {
                    const items = await window.offlineCapture.getQueueSnapshot();
                    const item = items.find(i => i.text === text);
                    return item?.status === 'synced';
                }
                """,
                captureText,
                new PageWaitForFunctionOptions { Timeout = 15_000, PollingInterval = 250 });

            // The capture is only real once it shows up as an actual Inbox note server-side —
            // the on-device "Synced" badge alone isn't proof the server has it.
            await page.GotoAsync($"{BaseUrl}/inbox");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.Locator(".inbox-card", new PageLocatorOptions { HasText = captureText })
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        });
    }
}
