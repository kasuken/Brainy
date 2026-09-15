using System.Globalization;
using System.Reflection;
using System.Resources;
using Brainy.Application.Localization;
using Microsoft.Extensions.Localization;

namespace Brainy.Web.Localization;

/// <summary>
/// Decorates an <see cref="IStringLocalizer"/> so that, only when <paramref name="isDevelopment"/>
/// is true, a lookup that resolved via fallback to the neutral (English) resource — because the
/// active non-default UI culture's own <c>.resx</c> is missing that key — is visibly marked
/// instead of rendering identically to a real translation. Production behavior is always the
/// plain fallback value: this class never throws and never changes what value is returned
/// outside Development. See docs/roadmap/release-7/17-localization.md: "Missing translations
/// fall back to English visibly in development, silently in production."
/// </summary>
internal sealed class FallbackVisibleStringLocalizer(IStringLocalizer inner, bool isDevelopment) : IStringLocalizer
{
    // ResourceManagerStringLocalizer does not expose its ResourceManager publicly; reflection is
    // the only way to ask "does this exact culture's resx have this key, ignoring fallback?"
    // Best-effort only: any failure (a future runtime rename included) just disables the dev
    // marker, never breaks resolution.
    private static readonly FieldInfo? ResourceManagerField = typeof(ResourceManagerStringLocalizer)
        .GetField("_resourceManager", BindingFlags.NonPublic | BindingFlags.Instance);

    public LocalizedString this[string name] => Annotate(name, inner[name]);

    public LocalizedString this[string name, params object[] arguments] => Annotate(name, inner[name, arguments]);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        inner.GetAllStrings(includeParentCultures);

    private LocalizedString Annotate(string name, LocalizedString resolved)
    {
        if (!isDevelopment || resolved.ResourceNotFound || !IsMissingForCurrentUiCulture(name))
            return resolved;

        return new LocalizedString(name, $"[missing translation: {name}]", resourceNotFound: false, resolved.SearchedLocation);
    }

    private bool IsMissingForCurrentUiCulture(string name)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (string.Equals(culture.Name, SupportedCultures.Default, StringComparison.OrdinalIgnoreCase))
            return false; // English is the baseline resx; there is no "translation" to be missing.

        if (ResourceManagerField?.GetValue(inner) is not ResourceManager resourceManager)
            return false;

        try
        {
            var exactCultureSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            return exactCultureSet?.GetString(name) is null;
        }
        catch (MissingManifestResourceException)
        {
            return false;
        }
    }
}
