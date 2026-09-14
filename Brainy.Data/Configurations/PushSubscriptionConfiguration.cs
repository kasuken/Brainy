using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscription");

        builder.HasKey(s => s.Id);

        builder.ConfigureUserOwnership();

        builder.Property(s => s.Endpoint)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(s => s.EndpointHash)
            .IsRequired()
            .HasMaxLength(64); // hex-encoded SHA-256 (32 bytes -> 64 hex chars)

        // A given endpoint identifies exactly one live subscription, so re-registering the
        // same device (e.g. the browser refreshed its push subscription) must update the
        // existing row rather than create a duplicate.
        builder.HasIndex(s => s.EndpointHash).IsUnique();

        builder.Property(s => s.P256dh)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.Auth)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.DeviceLabel)
            .HasMaxLength(200);
    }
}
