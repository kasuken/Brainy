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
        await page.GetByLabel(labelText, new PageGetByLabelOptions { Exact = true }).ClickAsync();
        // MudSelect's popover items don't consistently expose an accessible "option" role
        // across MudBlazor versions, so match on the rendered list item itself instead.
        await page.Locator(".mud-list-item", new PageLocatorOptions { HasText = optionText })
            .First.ClickAsync();
    }
}
