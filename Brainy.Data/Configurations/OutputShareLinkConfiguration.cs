using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class OutputShareLinkConfiguration : IEntityTypeConfiguration<OutputShareLink>
{
    public void Configure(EntityTypeBuilder<OutputShareLink> builder)
    {
        builder.ToTable("OutputShareLink");

        builder.HasKey(l => l.Id);

        builder.ConfigureUserOwnership();

        // Exactly one share link per output: enabling/regenerating replaces this row's hash
        // in place rather than creating a second, so a previously revoked link can never be
        // resurrected by looking up a stale row.
        builder.HasIndex(l => l.OutputId).IsUnique();

        builder.HasOne<Output>()
            .WithMany()
            .HasForeignKey(l => l.OutputId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(l => l.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // hex-encoded SHA-256 (32 bytes -> 64 hex chars)

        // The public share page looks up a presented token by its hash alone (it has no
        // other way to know which output is being requested), so this must be fast and unique.
        builder.HasIndex(l => l.TokenHash).IsUnique();
    }
}
