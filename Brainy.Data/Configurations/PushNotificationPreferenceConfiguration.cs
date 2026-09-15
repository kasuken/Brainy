using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class PushNotificationPreferenceConfiguration : IEntityTypeConfiguration<PushNotificationPreference>
{
    public void Configure(EntityTypeBuilder<PushNotificationPreference> builder)
    {
        builder.ToTable("PushNotificationPreference");

        builder.HasKey(p => p.Id);

        builder.ConfigureUserOwnership();
        builder.HasIndex(p => p.UserId).IsUnique();

        builder.Property(p => p.Enabled)
            .HasDefaultValue(false);

        builder.Property(p => p.DailyFocusNudgeEnabled)
            .HasDefaultValue(true);

        builder.Property(p => p.OverdueTaskEnabled)
            .HasDefaultValue(true);

        builder.Property(p => p.WeeklyReviewReminderEnabled)
            .HasDefaultValue(true);

        builder.Property(p => p.QuietHoursEnabled)
            .HasDefaultValue(true);

        builder.Property(p => p.QuietHoursStart)
            .HasDefaultValue(new TimeOnly(21, 0));

        builder.Property(p => p.QuietHoursEnd)
            .HasDefaultValue(new TimeOnly(8, 0));

        builder.Property(p => p.DailyFocusNudgeHour)
            .HasDefaultValue(8);

        builder.Property(p => p.WeeklyReviewDayOfWeek)
            .HasDefaultValue(DayOfWeek.Sunday);

        builder.Property(p => p.WeeklyReviewHour)
            .HasDefaultValue(17);
    }
}
