using Brainy.E2E.Tests.Infrastructure;
using Xunit;

namespace Brainy.E2E.Tests;

public sealed class SmokeTest(BrainyE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task RegisterNewUser_LandsOnTodayWithCaptureFab()
    {
        await RunAsync(async page =>
        {
            await page.RegisterNewUserAsync(BaseUrl);
        });
    }
}
