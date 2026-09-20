using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class McpAccessTokenConfiguration : IEntityTypeConfiguration<McpAccessToken>
{
    public void Configure(EntityTypeBuilder<McpAccessToken> builder)
    {
        builder.ToTable("McpAccessToken");

        builder.HasKey(t => t.Id);

        // ConfigureUserOwnership adds a NON-unique index on UserId: unlike the calendar feed
        // token, a user may hold several MCP tokens at once (one per client).
        builder.ConfigureUserOwnership();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // hex-encoded SHA-256 (32 bytes -> 64 hex chars)

        // The MCP endpoint looks up a presented token by its hash alone (it has no other way
        // to know which user is calling), so this must be fast and unique.
        builder.HasIndex(t => t.TokenHash).IsUnique();
    }
}
