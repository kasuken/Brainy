using System.Runtime.CompilerServices;

namespace Brainy.Web.Tests.ProductionSurface;

/// <summary>
/// Supplies the Stripe credentials every <c>WebApplicationFactory&lt;Program&gt;</c> in this
/// project needs in order to boot the real <c>Program.cs</c> pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <c>appsettings.json</c> ships <c>Billing:Provider = Stripe</c> because production runs live
/// Stripe, and <c>AddBilling</c> deliberately refuses to start when Stripe is selected without
/// credentials. Every production-surface test would otherwise fail at startup on a
/// configuration error that says nothing about the behaviour under test.
/// </para>
/// <para>
/// These arrive as environment variables rather than through each factory's
/// <c>ConfigureAppConfiguration</c>, because that seam is too late: <c>WebApplicationFactory</c>
/// applies test configuration when it intercepts <c>builder.Build()</c>, but <c>AddBilling</c>
/// reads configuration eagerly during <c>Program.cs</c>'s top-level statements, which run
/// first. Environment variables are already in <c>builder.Configuration</c> at
/// <c>CreateBuilder</c> time, so they are visible to that eager read. (Lazily-consumed test
/// overrides such as the connection string and <c>Seo:SiteOrigin</c> are unaffected and stay
/// in the factories.)
/// </para>
/// <para>
/// The values are syntactically valid but entirely fake. Supplying fakes rather than switching
/// tests to <c>Provider = None</c> keeps the test host on the same registration path production
/// uses, so a regression in the Stripe branch of <c>AddBilling</c> still fails the suite. No
/// test contacts the Stripe API: the provider only constructs a <c>StripeClient</c>, and
/// nothing here calls checkout, portal, or webhook parsing.
/// </para>
/// </remarks>
internal static class TestHostConfiguration
{
    [ModuleInitializer]
    internal static void SetStripeCredentials()
    {
        // Set unconditionally rather than only when unset: a real Billing__ApiKey exported in
        // a developer's shell must never reach a test host that could act on it.
        Environment.SetEnvironmentVariable("Billing__ApiKey", "sk_test_brainy_web_tests");
        Environment.SetEnvironmentVariable("Billing__WebhookSigningSecret", "whsec_brainy_web_tests");
    }
}
