using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public sealed class ProductEventConfiguration : IEntityTypeConfiguration<ProductEvent>
{
    public void Configure(EntityTypeBuilder<ProductEvent> builder)
    {
        builder.ToTable("ProductEvent");
        builder.HasKey(e => e.Id);
        builder.ConfigureUserOwnership();

        builder.Property(e => e.EventName)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.PropertiesJson)
            .HasMaxLength(2000);

        builder.HasIndex(e => new { e.UserId, e.EventName, e.OccurredAtUtc });
        builder.HasIndex(e => e.OccurredAtUtc);
    }
}
