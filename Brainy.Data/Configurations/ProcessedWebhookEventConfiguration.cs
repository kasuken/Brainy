using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        builder.ToTable("ProcessedWebhookEvent");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderEventId)
            .IsRequired()
            .HasMaxLength(200);

        // Enforces webhook idempotency at the database level: a second delivery of the
        // same provider event id fails the unique constraint instead of double-applying state.
        builder.HasIndex(e => e.ProviderEventId).IsUnique();

        builder.Property(e => e.EventType)
            .HasMaxLength(200);

        builder.Property(e => e.TargetUserId)
            .HasMaxLength(450);
    }
}
