namespace Brainy.Application.Localization;

/// <summary>
/// The fixed set of UI cultures Brainy ships translations for. Shared by the Web layer's
/// request-localization configuration and by <c>IUserCultureService</c>, which validates any
/// stored/selected culture id against this list. Adding a locale means adding it here plus its
/// resource files — nothing else in the negotiation/persistence pipeline changes.
/// </summary>
public static class SupportedCultures
{
    /// <summary>The default/fallback culture. Always first so it is also the negotiation default.</summary>
    public const string Default = "en-US";

    /// <summary>Second locale proving the localization pipeline end to end (issue #323).</summary>
    public const string Italian = "it-IT";

    /// <summary>All culture ids Brainy has resources for, in display order.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Italian];

    /// <summary>True when <paramref name="cultureId"/> is one of <see cref="All"/> (ordinal, case-insensitive).</summary>
    public static bool IsSupported(string? cultureId) =>
        !string.IsNullOrWhiteSpace(cultureId) &&
        All.Any(supported => string.Equals(supported, cultureId, StringComparison.OrdinalIgnoreCase));
}
