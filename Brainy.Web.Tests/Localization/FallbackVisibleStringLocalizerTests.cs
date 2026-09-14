using System.Globalization;
using AwesomeAssertions;
using Brainy.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Brainy.Web.Tests.Localization;

/// <summary>
/// Exercises <see cref="FallbackVisibleStringLocalizer"/> against a real generated resource
/// (SharedResource.resx / SharedResource.it-IT.resx), mirroring exactly how Program.cs wires
/// <see cref="IStringLocalizerFactory"/>. "FallbackDemoOnly" exists only in the neutral
/// (English) resx and is unused by any component — see its resx comment.
/// </summary>
public sealed class FallbackVisibleStringLocalizerTests
{
    private static IStringLocalizer<SharedResource> BuildLocalizer(bool isDevelopment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton<IStringLocalizerFactory>(sp => new FallbackVisibleStringLocalizerFactory(
            sp.GetRequiredService<IOptions<LocalizationOptions>>(),
            sp.GetRequiredService<ILoggerFactory>(),
            isDevelopment));

        return services.BuildServiceProvider().GetRequiredService<IStringLocalizer<SharedResource>>();
    }

    private static void RunUnderItalian(Action action)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void MissingItalianTranslation_InDevelopment_IsVisiblyFlagged()
    {
        var localizer = BuildLocalizer(isDevelopment: true);

        RunUnderItalian(() =>
        {
            LocalizedString value = localizer["FallbackDemoOnly"];
            value.ResourceNotFound.Should().BeFalse();
            ((string)value).Should().Be("[missing translation: FallbackDemoOnly]");
        });
    }

    [Fact]
    public void MissingItalianTranslation_InProduction_FallsBackSilentlyToEnglish()
    {
        var localizer = BuildLocalizer(isDevelopment: false);

        RunUnderItalian(() =>
        {
            LocalizedString value = localizer["FallbackDemoOnly"];
            ((string)value).Should().Be("Fallback demo only");
        });
    }

    [Fact]
    public void TranslatedKey_UnderItalian_UsesItalianRegardlessOfEnvironment()
    {
        var localizer = BuildLocalizer(isDevelopment: true);

        RunUnderItalian(() => ((string)localizer["Save"]).Should().Be("Salva"));
    }

    [Fact]
    public void TranslatedKey_UnderEnglish_IsNeverAnnotatedEvenInDevelopment()
    {
        var localizer = BuildLocalizer(isDevelopment: true);

        ((string)localizer["Save"]).Should().Be("Save");
    }

    [Fact]
    public void EntirelyUnknownKey_FallsBackToKeyNameWithoutThrowing()
    {
        var localizer = BuildLocalizer(isDevelopment: true);

        var act = () => localizer["NoSuchKeyAnywhere"];

        act.Should().NotThrow();
        LocalizedString value = localizer["NoSuchKeyAnywhere"];
        value.ResourceNotFound.Should().BeTrue();
        ((string)value).Should().Be("NoSuchKeyAnywhere");
    }
}
