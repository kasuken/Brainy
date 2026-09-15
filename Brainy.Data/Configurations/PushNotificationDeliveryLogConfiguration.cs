using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class PushNotificationDeliveryLogConfiguration : IEntityTypeConfiguration<PushNotificationDeliveryLog>
{
    public void Configure(EntityTypeBuilder<PushNotificationDeliveryLog> builder)
    {
        builder.ToTable("PushNotificationDeliveryLog");

        builder.HasKey(l => l.Id);

        builder.ConfigureUserOwnership();

        builder.Property(l => l.Category)
            .IsRequired();

        // The dispatcher's frequency-cap check looks up the most recent row per
        // (user, category), ordered by CreatedAtUtc.
        builder.HasIndex(l => new { l.UserId, l.Category, l.CreatedAtUtc });
    }
}
