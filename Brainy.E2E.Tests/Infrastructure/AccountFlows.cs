using Microsoft.Playwright;

namespace Brainy.E2E.Tests.Infrastructure;

/// <summary>A freshly registered Brainy account used by exactly one test.</summary>
public sealed record TestAccount(string Email, string Password);

public static class AccountFlows
{
    /// <summary>
    /// Registers a brand-new account (unique email per call) via the real Register page and
    /// waits for the post-registration redirect to Today ("/") to land — Identity's
    /// RequireConfirmedAccount is off by default (see BrainyAppFactory), so registering also
    /// signs the user in.
    /// </summary>
    public static async Task<TestAccount> RegisterNewUserAsync(this IPage page, string baseUrl)
    {
        var account = new TestAccount(
            Email: $"e2e-{Guid.NewGuid():N}@brainy.e2e.test",
            Password: "Correct-Horse-Battery-Staple-9!");

        await page.GotoAsync($"{baseUrl}/Account/Register");
        await page.Locator("#email").FillAsync(account.Email);
        await page.Locator("#password").FillAsync(account.Password);
        await page.Locator("#confirmPassword").FillAsync(account.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // Registration redirects to Today ("/"); the FAB is the most stable, always-present
        // marker that the authenticated shell (and its circuit) has rendered. Generous timeout:
        // this is often the very first request Kestrel handles in the whole run (JIT/view
        // compilation/EF model warm-up), which is materially slower than steady-state.
        await page.GetByRole(AriaRole.Button, new() { Name = "Capture" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 45_000 });

        // Today's OnInitializedAsync/JS-interop work (timezone detection, dashboard
        // preferences, the onboarding journey's own async visibility check) can still be
        // settling right after the FAB first appears; let that quiesce before the first
        // real interaction, or a click can land while Blazor is mid-render and re-diffing
        // the very button just clicked.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return account;
    }
}
