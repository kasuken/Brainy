namespace Brainy.Application.DTOs.Dashboard;

/// <summary>
/// Read-model for the current user's dashboard layout and notification preferences.
/// </summary>
public record UserDashboardPreferenceDto(
    Guid Id,
    string? WidgetOrder,
    string? CollapsedWidgets,
    int InboxWarningThreshold);
