using AwesomeAssertions;
using Brainy.Application.Email;
using Brainy.Application.Interfaces.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Email;

/// <summary>
/// Covers <c>DependencyInjection.AddEmail</c>'s provider selection: the <c>None</c> default
/// (issue #307 acceptance criterion 1) and that a misconfigured <c>Smtp</c> provider fails
/// fast at startup rather than registering a transport that would fail silently on first use.
/// </summary>
public sealed class EmailDependencyInjectionTests
{
    [Fact]
    public void AddEmail_WithNoConfiguration_RegistersNullEmailSenderAndDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        services.AddEmail(configuration);
        using var provider = services.BuildServiceProvider();

        var emailSender = provider.GetRequiredService<IEmailSender>();
        emailSender.Should().BeOfType<NullEmailSender>();
        provider.GetRequiredService<IAccountEmailService>().Should().NotBeNull();
    }

    [Fact]
    public void AddEmail_WithProviderExplicitlyNone_RegistersNullEmailSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "None" })
            .Build();

        services.AddEmail(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NullEmailSender>();
    }

    [Fact]
    public void AddEmail_WithSmtpProviderMissingHost_ThrowsAtRegistrationTimeInsteadOfFailingSilentlyLater()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Smtp",
                ["Email:FromAddress"] = "no-reply@brainy-me.com",
            })
            .Build();

        var act = () => services.AddEmail(configuration);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddEmail_WithSmtpProviderMissingFromAddress_ThrowsAtRegistrationTime()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Smtp",
                ["Email:SmtpHost"] = "smtp.example.com",
            })
            .Build();

        var act = () => services.AddEmail(configuration);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddEmail_WithValidSmtpConfiguration_RegistersASenderOtherThanTheNullProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Smtp",
                ["Email:SmtpHost"] = "smtp.example.com",
                ["Email:FromAddress"] = "no-reply@brainy-me.com",
            })
            .Build();

        services.AddEmail(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEmailSender>().Should().NotBeOfType<NullEmailSender>();
    }
}
