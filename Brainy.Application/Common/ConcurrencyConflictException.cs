namespace Brainy.Application.Common;

/// <summary>
/// Thrown when an update is rejected because the entity was modified by another
/// session (browser tab or circuit) after the caller loaded it. Callers should
/// inform the user and reload the latest data instead of overwriting it.
/// </summary>
public sealed class ConcurrencyConflictException(string entityName, Exception innerException)
    : Exception(
        $"The {entityName} was modified in another tab or window since it was loaded. " +
        "Reload it to see the latest version before saving again.",
        innerException);
