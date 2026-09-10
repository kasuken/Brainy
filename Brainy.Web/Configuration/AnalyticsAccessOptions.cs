namespace Brainy.Web.Configuration;

/// <summary>
/// Access control for the internal analytics dashboard. Brainy has no admin-role concept
/// today, so access is a simple, defaults-closed email allowlist rather than a new RBAC
/// system. An empty list means nobody can see the dashboard.
/// </summary>
public sealed class AnalyticsAccessOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "Analytics";

    /// <summary>Email addresses (case-insensitive) allowed to view the analytics dashboard.</summary>
    public string[] AdminEmails { get; set; } = [];
}
