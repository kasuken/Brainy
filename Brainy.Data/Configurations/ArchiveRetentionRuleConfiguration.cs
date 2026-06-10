using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ArchiveRetentionRuleConfiguration : IEntityTypeConfiguration<ArchiveRetentionRule>
{
    public void Configure(EntityTypeBuilder<ArchiveRetentionRule> builder)
    {
        builder.ToTable("ArchiveRetentionRule");
        builder.HasKey(r => r.Id);
        builder.ConfigureUserOwnership();
        builder.Property(r => r.EntityType).IsRequired().HasMaxLength(50);
        builder.HasIndex(r => new { r.UserId, r.EntityType }).IsUnique();
    }
}
