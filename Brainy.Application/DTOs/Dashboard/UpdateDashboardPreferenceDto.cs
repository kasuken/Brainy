namespace Brainy.Application.DTOs.Dashboard;

/// <summary>
/// Command DTO for updating the current user's dashboard layout and notification preferences.
/// </summary>
public record UpdateDashboardPreferenceDto(
    string? WidgetOrder,
    string? CollapsedWidgets,
    int InboxWarningThreshold);
