using Brainy.Application.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Options;
using Brainy.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Billing;

/// <summary>
/// Covers <c>DependencyInjection.AddBilling</c>'s provider selection: the <c>None</c> default
/// (issue #308 acceptance criterion "<c>Billing:Provider=None</c> still starts and behaves
/// exactly as today") and that a misconfigured <c>Stripe</c> provider fails fast at startup
/// rather than registering a billing provider that would fail silently on first checkout.
/// </summary>
public sealed class BillingDependencyInjectionTests
{
    private static IServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        return services;
    }

    [Fact]
    public void AddBilling_WithNoConfiguration_RegistersNullBillingProvider()
    {
        var services = BaseServices();
        var configuration = new ConfigurationBuilder().Build();

        services.AddBilling(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBillingProvider>().Should().BeOfType<NullBillingProvider>();
    }

    [Fact]
    public void AddBilling_WithProviderExplicitlyNone_RegistersNullBillingProvider()
    {
        var services = BaseServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:Provider"] = "None" })
            .Build();

        services.AddBilling(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBillingProvider>().Should().BeOfType<NullBillingProvider>();
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("WebhookSigningSecret")]
    [InlineData("ProPriceId")]
    [InlineData("AppBaseUrl")]
    public void AddBilling_WithStripeProviderMissingARequiredSetting_ThrowsAtRegistrationTime(string missingKey)
    {
        var services = BaseServices();
        var settings = new Dictionary<string, string?>
        {
            ["Billing:Provider"] = "Stripe",
            ["Billing:ApiKey"] = "sk_test_fake",
            ["Billing:WebhookSigningSecret"] = "whsec_fake",
            ["Billing:ProPriceId"] = "price_fake",
            ["Billing:AppBaseUrl"] = "https://app.example.test",
        };
        settings.Remove($"Billing:{missingKey}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var act = () => services.AddBilling(configuration);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddBilling_WithValidStripeConfiguration_RegistersStripeBillingProvider()
    {
        var services = BaseServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "Stripe",
                ["Billing:ApiKey"] = "sk_test_fake",
                ["Billing:WebhookSigningSecret"] = "whsec_fake",
                ["Billing:ProPriceId"] = "price_fake",
                ["Billing:AppBaseUrl"] = "https://app.example.test",
            })
            .Build();

        services.AddBilling(configuration);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBillingProvider>().Should().NotBeOfType<NullBillingProvider>();
    }
}
