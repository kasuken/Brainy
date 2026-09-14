using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Push;

/// <summary>
/// The entire content of one push notification. <see cref="Heading"/> and <see cref="Body"/>
/// must never contain note or task content (issue #315's guardrail) — only fixed category
/// labels and counts, e.g. "Overdue tasks" / "You have 3 overdue task(s).". A push payload
/// traverses a third-party push service (Google/Mozilla/Apple), so anything placed in it
/// leaves Brainy's infrastructure. <see cref="Category"/> is null for an ad hoc test
/// notification, which belongs to none of the three scheduled categories.
/// </summary>
public record PushNotificationPayload(string Heading, string Body, PushNotificationCategory? Category);
