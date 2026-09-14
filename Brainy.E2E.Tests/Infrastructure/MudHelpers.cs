using Microsoft.Playwright;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>Small helpers for driving MudBlazor controls the way a real user would.</summary>
public static class MudHelpers
{
    /// <summary>
    /// Opens a <c>MudSelect</c> found by its floating label and picks the option with the
    /// given exact text from the popover it opens.
    /// </summary>
    public static async Task SelectMudOptionAsync(this IPage page, string labelText, string optionText)
    {
        // MudSelect's <label for="..."> sometimes points at the underlying type="hidden"
        // input (it does whenever a value is already selected, e.g. a dialog that
        // auto-selects the only available option) rather than the visible, clickable
        // display div next to it — GetByLabel(...).ClickAsync() then waits forever for a
        // hidden element to become visible. Click the whole MudSelect container (identified
        // by the same label) instead: it is always visible, and MudBlazor's open-popover
        // handler responds to a click anywhere in it.
        // Some MudSelects use Label="..." (renders a real <label> element); others (e.g. the
        // Inbox processor's project/area/resource pickers) use a plain aria-label attribute
        // instead — match either so this helper works for both.
        await page.Locator(".mud-select", new PageLocatorOptions
        {
            Has = page.Locator($"label:text-is('{labelText}'), [aria-label='{labelText}']")
        }).First.ClickAsync();

        // MudSelect's popover items don't consistently expose an accessible "option" role
        // across MudBlazor versions, so match on the rendered list item itself instead.
        await page.Locator(".mud-list-item", new PageLocatorOptions { HasText = optionText })
            .First.ClickAsync();
    }
}
