namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Resolves and persists the current user's UI culture (language) preference. Governs display
/// only — see <see cref="IUserTimeZoneService"/> for the (separate) calendar-day concept, and
/// note that neither service changes how due dates or audit timestamps are stored.
/// </summary>
public interface IUserCultureService
{
    /// <summary>
    /// Returns the current user's stored culture id (e.g. "it-IT"), or null if the user has
    /// never had one negotiated or selected yet.
    /// </summary>
    Task<string?> GetCultureIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's UI culture after validating it is one of
    /// <see cref="Brainy.Application.Localization.SupportedCultures.All"/>.
    /// </summary>
    Task SetCultureIdAsync(string cultureId, CancellationToken cancellationToken = default);
}
