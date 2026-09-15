using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class CalendarFeedTokenConfiguration : IEntityTypeConfiguration<CalendarFeedToken>
{
    public void Configure(EntityTypeBuilder<CalendarFeedToken> builder)
    {
        builder.ToTable("CalendarFeedToken");

        builder.HasKey(t => t.Id);

        builder.ConfigureUserOwnership();

        // Exactly one feed token per user: regenerating replaces this row's hash in place
        // rather than creating a second, so an old token can never be resurrected by looking
        // up a stale row.
        builder.HasIndex(t => t.UserId).IsUnique();

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // hex-encoded SHA-256 (32 bytes -> 64 hex chars)

        // The feed endpoint looks up a presented token by its hash alone (it has no other
        // way to know which user is calling), so this must be fast and unique.
        builder.HasIndex(t => t.TokenHash).IsUnique();
    }
}
