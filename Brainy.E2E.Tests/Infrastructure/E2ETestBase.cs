using Microsoft.Playwright;
using Xunit;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for every E2E test class. Owns the per-test <see cref="IBrowserContext"/>/<see
/// cref="IPage"/> lifecycle (fresh context per test — no shared cookies/storage between
/// tests), tracing, and on-failure trace/screenshot capture.
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase(BrainyE2EFixture fixture)
{
    protected string BaseUrl => fixture.BaseUrl;

    /// <summary>
    /// Runs <paramref name="testBody"/> against a fresh, isolated browser context. Tracing
    /// (screenshots + DOM snapshots + sources) always runs; the trace and a final screenshot
    /// are written to disk ONLY if the body throws, under
    /// <c>&lt;output dir&gt;/test-results/&lt;testName&gt;/</c> — this is what CI uploads as an
    /// artifact on failure (see .github/workflows/ci.yml's e2e job).
    /// </summary>
    protected async Task RunAsync(
        Func<IPage, Task> testBody,
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            BaseURL = fixture.BaseUrl
        });

        // Several pages (Today, Projects, …) auto-open a first-visit "how this page works"
        // tour once per browser storage (see PageHelpButton.razor / brainyHelp.js) — a real,
        // deliberate product feature, not a bug. Left alone, it races the test's own first
        // interaction on that page (it opens from OnAfterRenderAsync, exactly when a test's
        // first click on that page also becomes possible) and its modal backdrop swallows
        // whatever the test just clicked. Rather than a page-specific dismiss-if-present
        // step (racy, and different per guide), patch localStorage so brainyHelp.hasSeen(...)
        // always reports "already seen" — same effect as a returning visitor, purely on the
        // test's own browser storage, no application code touched.
        await context.AddInitScriptAsync(
            """
            (() => {
                const originalGetItem = Storage.prototype.getItem;
                Storage.prototype.getItem = function (key) {
                    if (typeof key === 'string' && key.indexOf('brainy.help.') === 0) return '1';
                    return originalGetItem.call(this, key);
                };
            })();
            """);

        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        var page = await context.NewPageAsync();

        try
        {
            await testBody(page);
        }
        catch (Exception)
        {
            var resultsDir = Path.Combine(AppContext.BaseDirectory, "test-results", testName);
            Directory.CreateDirectory(resultsDir);

            try
            {
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(resultsDir, "failure.png"),
                    FullPage = true
                });
            }
            catch
            {
                // Best-effort: the page/context may already be unusable if the failure was a
                // crash/navigation error. The trace below is the more important artifact.
            }

            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine(resultsDir, "trace.zip")
            });
            await context.CloseAsync();
            throw;
        }

        await context.Tracing.StopAsync();
        await context.CloseAsync();
    }
}
