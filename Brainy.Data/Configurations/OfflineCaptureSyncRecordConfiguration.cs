using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class OfflineCaptureSyncRecordConfiguration : IEntityTypeConfiguration<OfflineCaptureSyncRecord>
{
    public void Configure(EntityTypeBuilder<OfflineCaptureSyncRecord> builder)
    {
        builder.ToTable("OfflineCaptureSyncRecord");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.UserId)
            .IsRequired()
            .HasMaxLength(450);

        // Enforces "syncs exactly once" at the database level, scoped per user (AGENTS.md:
        // every mutation must be scoped to the authenticated user): a retried sync of the
        // same client-generated key, for the same user, fails the unique constraint instead
        // of creating a second Note. Scoping by user too (rather than a global unique index
        // on IdempotencyKey alone) also means an astronomically unlikely cross-user GUID
        // collision can never cause one user's genuine capture to be silently dropped as
        // "already synced" because another user's key happened to match.
        builder.HasIndex(r => new { r.UserId, r.IdempotencyKey }).IsUnique();
    }
}
