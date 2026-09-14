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
            // Two Playwright-level ways to simulate "the connection is gone" turned out
            // unusable in this environment:
            //  - BrowserContext.SetOfflineAsync(true) does not sever an already-established
            //    WebSocket at all here — the reconnect UI never appeared even after 45s
            //    (well past SignalR's 30s default server-timeout watchdog).
            //  - Page.RouteWebSocketAsync (proxy the socket, then close the proxy) does force
            //    a real close, but crashed the whole Playwright<->browser connection outright
            //    within seconds, taking every other test in the run down with it — reproduced
            //    consistently across several variations.
            // Instead, capture Blazor's own real WebSocket object via a plain JS
            // constructor patch (installed before any page script runs) and call the
            // standard WebSocket.close() on it directly from the page's own JS context. This
            // is a real close of the real socket SignalR is using — no Playwright network
            // interception involved, and no proxy layer to be unstable.
            await page.Context.AddInitScriptAsync(
                """
                (() => {
                    window.__capturedSockets = [];
                    const NativeWebSocket = window.WebSocket;
                    window.WebSocket = new Proxy(NativeWebSocket, {
                        construct(target, args) {
                            const socket = new target(...args);
                            if (String(args[0]).includes('/_blazor')) {
                                window.__capturedSockets.push(socket);
                            }
                            return socket;
                        }
                    });
                })();
                """);

            await page.RegisterNewUserAsync(BaseUrl);

            var reconnectModal = page.Locator("#components-reconnect-modal[open]");

            // Close the real, live SignalR WebSocket — indistinguishable to Blazor's client
            // runtime from the underlying connection actually dropping.
            await page.EvaluateAsync("() => window.__capturedSockets.at(-1)?.close()");

            await Expect(reconnectModal).ToBeVisibleAsync(new() { Timeout = 15_000 });

            // Blazor's default reconnection backoff retries automatically; its new WebSocket
            // is captured by the same init script (ordinary browser networking, no mock
            // involved), so nothing further is needed for the retry to succeed.
            await Expect(reconnectModal).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

            // The circuit being visually reconnected isn't proof it is actually usable again —
            // drive a real, server-handled interaction to confirm the app recovered rather than
            // just hid the modal.
            await page.CaptureNoteAsync($"Post-reconnect capture {Guid.NewGuid():N}");
        });
    }
}
