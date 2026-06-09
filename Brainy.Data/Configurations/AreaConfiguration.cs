using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("Area");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.HasIndex(a => a.Name);

        builder.HasMany(a => a.Projects)
            .WithOne(p => p.Area)
            .HasForeignKey(p => p.AreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.Resources)
            .WithOne(r => r.Area)
            .HasForeignKey(r => r.AreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.Notes)
            .WithOne(n => n.Area)
            .HasForeignKey(n => n.AreaId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
