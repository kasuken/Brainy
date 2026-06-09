using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tag");

        builder.HasKey(t => t.Id);

        builder.ConfigureUserOwnership();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Color)
            .HasMaxLength(20);

        // Tag names are unique per owning user.
        builder.HasIndex(t => new { t.UserId, t.Name }).IsUnique();
    }
}
