using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resource");

        builder.HasKey(r => r.Id);

        builder.ConfigureUserOwnership();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Description)
            .HasMaxLength(2000);

        builder.HasIndex(r => r.Name);

        builder.HasMany(r => r.Notes)
            .WithOne(n => n.Resource)
            .HasForeignKey(n => n.ResourceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
