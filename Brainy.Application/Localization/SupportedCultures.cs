using System.Globalization;

namespace Brainy.Application.Localization;

/// <summary>
/// The fixed set of UI cultures Brainy ships translations for. Shared by the Web layer's
/// request-localization configuration and by <c>IUserCultureService</c>, which validates any
/// stored/selected culture id against this list. Adding a locale means adding it here plus its
/// resource files — nothing else in the negotiation/persistence pipeline changes.
/// </summary>
public static class SupportedCultures
{
    /// <summary>
    /// The default/fallback culture's storage id. Deliberately resolves to
    /// <see cref="CultureInfo.InvariantCulture"/> (see <see cref="Resolve"/>), not a real
    /// "en-US" <see cref="CultureInfo"/>: before this issue the app had no request-localization
    /// middleware at all, so every request already ran under whichever ambient culture the
    /// process started with — invariant, in every environment this app actually runs in (no
    /// LANG/ICU data configured). Resolving the default id to a real "en-US" CultureInfo would
    /// be a genuine, if subtle, formatting change for English users (e.g. invariant's
    /// "MM/dd/yyyy" short date pattern vs. real en-US's "M/d/yyyy") — exactly the kind of
    /// regression the "no behaviour change for English users" guardrail exists to catch.
    /// </summary>
    public const string Default = "en-US";

    /// <summary>Second locale proving the localization pipeline end to end (issue #323).</summary>
    public const string Italian = "it-IT";

    /// <summary>All culture ids Brainy has resources for, in display order.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Italian];

    /// <summary>True when <paramref name="cultureId"/> is one of <see cref="All"/> (ordinal, case-insensitive).</summary>
    public static bool IsSupported(string? cultureId) =>
        !string.IsNullOrWhiteSpace(cultureId) &&
        All.Any(supported => string.Equals(supported, cultureId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves a stored/selected culture id to the actual <see cref="CultureInfo"/> to apply.
    /// <see cref="Default"/> maps to <see cref="CultureInfo.InvariantCulture"/> (see that
    /// constant's remarks); every other supported id maps to its real culture.
    /// </summary>
    public static CultureInfo Resolve(string cultureId) =>
        string.Equals(cultureId, Default, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(cultureId);

    /// <summary>
    /// The BCP-47/ISO 639-1 language subtag for an <c>&lt;html lang&gt;</c> attribute.
    /// <see cref="CultureInfo.InvariantCulture"/>'s own <see cref="CultureInfo.TwoLetterISOLanguageName"/>
    /// is the nonsensical "iv", so it is special-cased to "en" (see <see cref="Default"/>'s remarks
    /// for why the default resolves to invariant in the first place).
    /// </summary>
    public static string HtmlLangTag(CultureInfo culture) =>
        culture.Equals(CultureInfo.InvariantCulture) ? "en" : culture.TwoLetterISOLanguageName;

    /// <summary>
    /// The inverse of <see cref="Resolve"/>: maps a negotiated <see cref="CultureInfo"/> (from
    /// RequestLocalizationMiddleware, or invariant) back to a storable id, so a never-set
    /// preference can be persisted as the negotiated culture without ever writing invariant's
    /// empty name into storage. Anything not in <see cref="All"/> — including invariant —
    /// normalizes to <see cref="Default"/>.
    /// </summary>
    public static string NormalizeToStorableId(CultureInfo culture) =>
        All.FirstOrDefault(id => string.Equals(id, culture.Name, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
