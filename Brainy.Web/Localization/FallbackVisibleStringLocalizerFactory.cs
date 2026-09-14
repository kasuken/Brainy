using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Brainy.Web.Localization;

/// <summary>
/// Replaces the default <see cref="IStringLocalizerFactory"/> registration so every
/// <c>IStringLocalizer&lt;T&gt;</c> resolved by DI is wrapped in <see cref="FallbackVisibleStringLocalizer"/>.
/// Register after <c>AddLocalization</c> in Program.cs so this registration wins.
/// </summary>
internal sealed class FallbackVisibleStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly ResourceManagerStringLocalizerFactory _inner;
    private readonly bool _isDevelopment;

    public FallbackVisibleStringLocalizerFactory(
        IOptions<LocalizationOptions> localizationOptions,
        ILoggerFactory loggerFactory,
        bool isDevelopment)
    {
        _inner = new ResourceManagerStringLocalizerFactory(localizationOptions, loggerFactory);
        _isDevelopment = isDevelopment;
    }

    public IStringLocalizer Create(Type resourceSource) => Wrap(_inner.Create(resourceSource));

    public IStringLocalizer Create(string baseName, string location) => Wrap(_inner.Create(baseName, location));

    private IStringLocalizer Wrap(IStringLocalizer localizer) =>
        new FallbackVisibleStringLocalizer(localizer, _isDevelopment);
}
