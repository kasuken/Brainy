using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// Append-only record of one push-notification category having been dispatched (attempted)
/// for a user. The background dispatcher uses the most recent row per (user, category) to
/// enforce the frequency cap — at most once per local calendar day for
/// <see cref="PushNotificationCategory.DailyFocusNudge"/> and
/// <see cref="PushNotificationCategory.OverdueTask"/>, at most once per local calendar week
/// for <see cref="PushNotificationCategory.WeeklyReviewReminder"/> — independent of how many
/// of the user's devices actually received it.
/// </summary>
public class PushNotificationDeliveryLog : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    public PushNotificationCategory Category { get; set; }
}
