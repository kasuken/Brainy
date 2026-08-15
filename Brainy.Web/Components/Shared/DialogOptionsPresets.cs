using MudBlazor;

namespace Brainy.Web.Components.Shared;

/// <summary>
/// Centralized <see cref="DialogOptions"/> presets for new Brainy dialogs.
/// These presets are additive helpers and do not change any existing dialog behavior
/// until a caller explicitly opts in.
/// <example>
/// var dialog = await DialogService.ShowAsync&lt;TaskEditorDialog&gt;(
///     "Edit Task",
///     parameters,
///     DialogOptionsPresets.Default);
/// </example>
/// </summary>
public static class DialogOptionsPresets
{
    /// <summary>
    /// Standard small dialog used by the majority of current page and editor flows.
    /// Explicitly enables full width, backdrop dismissal, and the Escape key.
    /// </summary>
    public static readonly DialogOptions Default = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        BackdropClick = true,
        CloseOnEscapeKey = true
    };

    /// <summary>
    /// Medium-width dialog for richer editors that still follow the standard dismiss behavior.
    /// </summary>
    public static readonly DialogOptions Medium = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        BackdropClick = true,
        CloseOnEscapeKey = true
    };

    /// <summary>
    /// Large editor-style dialog for immersive flows where accidental dismissal should be avoided.
    /// </summary>
    public static readonly DialogOptions LargeEditor = new()
    {
        MaxWidth = MaxWidth.Large,
        FullWidth = true,
        BackdropClick = false,
        CloseOnEscapeKey = false,
        CloseButton = false
    };

    /// <summary>
    /// Confirmation dialog preset for high-intent actions.
    /// Keeps the dialog compact while disabling backdrop dismissal.
    /// </summary>
    public static readonly DialogOptions Confirmation = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        BackdropClick = false,
        CloseOnEscapeKey = true,
        CloseButton = false
    };
}

/// <summary>
/// Convenience extensions for dialog flows that should share consistent behavior.
/// <example>
/// var confirmed = await DialogService.ShowConfirmationAsync(
///     "Delete task?",
///     "This cannot be undone.",
///     "Delete");
/// </example>
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Shows a standardized confirmation message box using MudBlazor's built-in message box support.
    /// Returns <see langword="true"/> only when the confirm button is selected.
    /// </summary>
    /// <param name="dialogService">The dialog service used to open the confirmation dialog.</param>
    /// <param name="title">The confirmation title shown at the top of the dialog.</param>
    /// <param name="message">The confirmation message shown in the dialog body.</param>
    /// <param name="confirmText">The confirm button label. Defaults to <c>Confirm</c>.</param>
    /// <param name="cancelText">The cancel button label. Defaults to <c>Cancel</c>.</param>
    /// <returns><see langword="true"/> when confirmed; otherwise <see langword="false"/>.</returns>
    /// <example>
    /// var confirmed = await DialogService.ShowConfirmationAsync(
    ///     "Archive project?",
    ///     "Active tasks will move into archived context.",
    ///     "Archive");
    /// </example>
    public static async Task<bool> ShowConfirmationAsync(
        this IDialogService dialogService,
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel")
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);

        var result = await dialogService.ShowMessageBox(
            title,
            message,
            yesText: confirmText,
            noText: null,
            cancelText: cancelText,
            options: DialogOptionsPresets.Confirmation);

        return result == true;
    }
}
