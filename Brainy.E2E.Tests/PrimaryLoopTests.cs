using Brainy.E2E.Tests.Infrastructure;
using Xunit;

namespace Brainy.E2E.Tests;

/// <summary>
/// Covers the primary loop from docs/roadmap/release-7/15-e2e-tests.md: register, capture,
/// process from Inbox, create a task, set current focus, complete it, and generate an output —
/// end to end through a real browser against a real Kestrel-hosted app and real SQL Server.
/// </summary>
public sealed class PrimaryLoopTests(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task CaptureToOutputLoop_CompletesEndToEnd()
    {
        await RunAsync(async page =>
        {
            var unique = Guid.NewGuid().ToString("N")[..8];
            var noteText = $"Renew the annual insurance policy {unique}";
            var areaName = $"Household {unique}";
            var projectName = $"Insurance renewal {unique}";
            var taskTitle = $"Call the insurance broker {unique}";
            var outputTitle = $"Insurance renewal summary {unique}";

            await page.RegisterNewUserAsync(BaseUrl);

            // Capture: a fleeting thought lands in the Inbox, unfiled.
            await page.CaptureNoteAsync(noteText);

            // Organize: give it a home before it can become a task (PARA requires a project;
            // a project requires an area).
            await page.CreateAreaAsync(BaseUrl, areaName);
            await page.CreateProjectAsync(BaseUrl, projectName, areaName);

            // Process from Inbox: the capture becomes a filed, distilled note on the project.
            await page.ProcessNoteIntoProjectAsync(BaseUrl, noteText, projectName);

            // Create a task, set it as the current focus, and complete it.
            await page.CreateTaskFromTodayAsync(BaseUrl, taskTitle, projectName);
            await page.SetCurrentFocusAsync(BaseUrl, taskTitle);
            await page.CompleteCurrentFocusAsync(BaseUrl);

            // Generate an output: a real deliverable comes out the other end of the loop.
            await page.CreateOutputAsync(BaseUrl, outputTitle);
        });
    }
}
