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

        builder.Property(r => r.Topic)
            .HasMaxLength(200);

        builder.HasIndex(r => r.Name);
        builder.HasIndex(r => r.Topic);

        builder.HasMany(r => r.Notes)
            .WithOne(n => n.Resource)
            .HasForeignKey(n => n.ResourceId)
            .OnDelete(DeleteBehavior.SetNull);

        // Resource <-> Tag many-to-many via skip navigation.
        builder.HasMany(r => r.Tags)
            .WithMany(t => t.Resources)
            .UsingEntity(j => j.ToTable("ResourceTag"));
    }
}
