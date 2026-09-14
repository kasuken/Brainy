using Brainy.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Brainy.E2E.Tests;

/// <summary>
/// Covers Blazor Interactive Server circuit reconnection — the SignalR connection drops and
/// Blazor's built-in reconnect UI (Components/Layout/ReconnectModal.razor) takes over, then the
/// app becomes interactive again once the circuit reconnects. Unit/component tests cannot
/// observe this: it requires a real SignalR connection and a real severed connection.
/// </summary>
public sealed class CircuitReconnectionTests(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task CircuitDisconnectAndReconnect_ShowsReconnectUiThenRecovers()
    {
        await RunAsync(async page =>
        {
            await page.RegisterNewUserAsync(BaseUrl);

            var reconnectModal = page.Locator("#components-reconnect-modal[open]");

            // A real network condition (Chromium's offline emulation): the already-open
            // SignalR connection is black-holed, not merely told to close. Blazor's SignalR
            // client has its own keep-alive/server-timeout watchdog (default 30s) independent
            // of the OS/transport actually noticing the drop, so it declares the circuit lost
            // and shows the reconnect UI once that watchdog expires — no faked event needed,
            // just enough real time for the existing mechanism to notice.
            //
            // (An earlier version of this test tried to force an immediate drop by routing the
            // WebSocket through Page.RouteWebSocketAsync and closing that proxy — technically
            // closer to "instant", but calling WebSocketRoute.CloseAsync while Blazor's client
            // was mid-reconnect intermittently crashed the whole Playwright<->browser
            // connection, taking every other test in the run down with it. Not worth the
            // instability for a few seconds saved.)
            await page.Context.SetOfflineAsync(true);

            await Expect(reconnectModal).ToBeVisibleAsync(new() { Timeout = 45_000 });

            await page.Context.SetOfflineAsync(false);

            // Blazor's default reconnection backoff retries automatically once the network is
            // back.
            await Expect(reconnectModal).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

            // The circuit being visually reconnected isn't proof it is actually usable again —
            // drive a real, server-handled interaction to confirm the app recovered rather than
            // just hid the modal.
            await page.CaptureNoteAsync($"Post-reconnect capture {Guid.NewGuid():N}");
        });
    }
}
