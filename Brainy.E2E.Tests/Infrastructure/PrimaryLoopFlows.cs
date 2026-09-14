using Microsoft.Playwright;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>
/// Page-object-style helpers for the primary capture-to-output loop: capture, process from
/// Inbox, create an area/project/task, set focus, complete, generate an output. Each helper
/// waits on the real, user-observable result of the action (a dialog closing, a row
/// appearing) rather than a fixed delay.
/// </summary>
public static class PrimaryLoopFlows
{
    public static async Task CaptureNoteAsync(this IPage page, string text)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Capture", Exact = true })
            .ClickAsync();
        var textarea = page.GetByLabel("Capture text");
        await textarea.WaitForAsync();
        await textarea.FillAsync(text);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save to Inbox" }).ClickAsync();

        // The dialog closes once the note is actually persisted.
        await textarea.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    public static async Task CreateAreaAsync(this IPage page, string baseUrl, string name)
    {
        await page.GotoAsync($"{baseUrl}/areas");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Area" }).First.ClickAsync();

        var nameField = page.GetByLabel("Name", new PageGetByLabelOptions { Exact = true });
        await nameField.WaitForAsync();
        await nameField.FillAsync(name);
        // MudTextField validates (and reports up to MudForm's bound _valid, which the submit
        // button's Disabled state reads) on blur, not on every keystroke — Tab off the field
        // so the button actually becomes enabled before we try to click it.
        await nameField.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create area" }).ClickAsync();

        await page.GetByText(name, new PageGetByTextOptions { Exact = false })
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
    }

    public static async Task CreateProjectAsync(this IPage page, string baseUrl, string name, string areaName)
    {
        await page.GotoAsync($"{baseUrl}/projects");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Project" }).First.ClickAsync();

        var nameField = page.GetByLabel("Name", new PageGetByLabelOptions { Exact = true });
        await nameField.WaitForAsync();
        await nameField.FillAsync(name);
        await page.SelectMudOptionAsync("Area", areaName);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create project" }).ClickAsync();

        await page.GetByText(name, new PageGetByTextOptions { Exact = false })
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
    }

    /// <summary>Processes the Inbox note whose card contains <paramref name="noteText"/> into
    /// the given project, marking it Distilled (the "this became something real" status).</summary>
    public static async Task ProcessNoteIntoProjectAsync(
        this IPage page, string baseUrl, string noteText, string projectName)
    {
        await page.GotoAsync($"{baseUrl}/inbox");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = page.Locator(".inbox-card", new PageLocatorOptions { HasText = noteText });
        await card.WaitForAsync();
        await card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Process", Exact = true })
            .ClickAsync();

        // Project is the default-selected PARA category, but select it explicitly so the
        // test doesn't depend on that default.
        await page.Locator(".pnd__cat-label", new PageLocatorOptions { HasText = "Project" }).ClickAsync();
        await page.SelectMudOptionAsync("Assign to project", projectName);
        await page.Locator(".pnd__status-label", new PageLocatorOptions { HasText = "Distilled" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Process note" }).ClickAsync();

        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }

    public static async Task<string> CreateTaskFromTodayAsync(
        this IPage page, string baseUrl, string title, string projectName)
    {
        await page.GotoAsync(baseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Two equivalent "New Task" affordances exist on Today (a top nav chip and the quick
        // actions bar) — both call the same handler; pick either, deterministically.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Task", Exact = true })
            .First.ClickAsync();

        var titleField = page.GetByLabel("Title", new PageGetByLabelOptions { Exact = true });
        await titleField.WaitForAsync();
        await titleField.FillAsync(title);
        await page.SelectMudOptionAsync("Project", projectName);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Task" }).ClickAsync();

        await titleField.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
        return title;
    }

    public static async Task SetCurrentFocusAsync(this IPage page, string baseUrl, string taskTitle)
    {
        await page.GotoAsync(baseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Set Focus", Exact = true })
            .ClickAsync();

        var row = page.Locator(".fp-row", new PageLocatorOptions { HasText = taskTitle });
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await row.ClickAsync();

        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }

    public static async Task CompleteCurrentFocusAsync(this IPage page, string baseUrl)
    {
        await page.GotoAsync(baseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var completeButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Mark Complete" });
        await completeButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await completeButton.ClickAsync();

        await completeButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }

    public static async Task CreateOutputAsync(this IPage page, string baseUrl, string title)
    {
        await page.GotoAsync($"{baseUrl}/outputs");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Output" }).First.ClickAsync();

        var titleField = page.GetByLabel("Title", new PageGetByLabelOptions { Exact = true });
        await titleField.WaitForAsync();
        await titleField.FillAsync(title);
        await titleField.PressAsync("Tab"); // blur so MudForm re-validates and enables Create
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Output" }).ClickAsync();

        await titleField.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15_000 });
    }
}
