using MudBlazor;

namespace Brainy.Web.Components.Shared;

/// <summary>
/// Centralized snackbar helpers for consistent message tone, iconography, and timing.
/// These helpers are additive and can be adopted gradually without changing current call sites.
/// <example>
/// Snackbar.ShowSuccess("Task created.");
/// Snackbar.ShowError("Could not save the task.");
/// </example>
/// </summary>
public static class SnackbarExtensions
{
    private const int DefaultDurationMs = 4000;
    private const int WarningDurationMs = 5000;
    private const int ErrorDurationMs = 7000;

    /// <summary>
    /// Shows a success snackbar with the shared success icon and default duration.
    /// </summary>
    /// <param name="snackbar">The snackbar service.</param>
    /// <param name="message">The message to show.</param>
    /// <example>
    /// Snackbar.ShowSuccess("Task created.");
    /// </example>
    public static void ShowSuccess(this ISnackbar snackbar, string message) =>
        Show(snackbar, message, Severity.Success, Icons.Material.Filled.CheckCircle, DefaultDurationMs);

    /// <summary>
    /// Shows an error snackbar with the shared error icon and a longer visible duration.
    /// </summary>
    /// <param name="snackbar">The snackbar service.</param>
    /// <param name="message">The message to show.</param>
    /// <example>
    /// Snackbar.ShowError("Could not save the task.");
    /// </example>
    public static void ShowError(this ISnackbar snackbar, string message) =>
        Show(snackbar, message, Severity.Error, Icons.Material.Filled.Error, ErrorDurationMs);

    /// <summary>
    /// Shows a warning snackbar with the shared warning icon and slightly extended duration.
    /// </summary>
    /// <param name="snackbar">The snackbar service.</param>
    /// <param name="message">The message to show.</param>
    /// <example>
    /// Snackbar.ShowWarning("Voice capture is not available in this browser.");
    /// </example>
    public static void ShowWarning(this ISnackbar snackbar, string message) =>
        Show(snackbar, message, Severity.Warning, Icons.Material.Filled.Warning, WarningDurationMs);

    /// <summary>
    /// Shows an informational snackbar with the shared info icon and default duration.
    /// </summary>
    /// <param name="snackbar">The snackbar service.</param>
    /// <param name="message">The message to show.</param>
    /// <example>
    /// Snackbar.ShowInfo("Task moved to In Progress.");
    /// </example>
    public static void ShowInfo(this ISnackbar snackbar, string message) =>
        Show(snackbar, message, Severity.Info, Icons.Material.Filled.Info, DefaultDurationMs);

    private static void Show(ISnackbar snackbar, string message, Severity severity, string icon, int visibleStateDuration)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        snackbar.Add(
            message,
            severity,
            options =>
            {
                options.Icon = icon;
                options.VisibleStateDuration = visibleStateDuration;
            });
    }
}
