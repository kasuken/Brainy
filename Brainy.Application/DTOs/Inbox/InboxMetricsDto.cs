namespace Brainy.Application.DTOs.Inbox;

/// <summary>Snapshot of inbox health statistics for the current user.</summary>
public record InboxMetricsDto(
    /// <summary>Notes currently in Inbox (unprocessed).</summary>
    int WaitingCount,
    /// <summary>Notes captured (created with Inbox status) today in UTC.</summary>
    int CapturedTodayCount,
    /// <summary>Age in days of the oldest unprocessed inbox note. Null when inbox is empty.</summary>
    int? OldestItemAgeInDays,
    /// <summary>Average days between capture and processing. Null when no data.</summary>
    double? AvgProcessingDays);
