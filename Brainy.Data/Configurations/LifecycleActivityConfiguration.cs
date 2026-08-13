using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public sealed class LifecycleActivityConfiguration : IEntityTypeConfiguration<LifecycleActivity>
{
    public void Configure(EntityTypeBuilder<LifecycleActivity> builder)
    {
        builder.ToTable("LifecycleActivity");
        builder.HasKey(a => a.Id);
        builder.ConfigureUserOwnership();

        builder.Property(a => a.ActivityType)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(a => a.Context)
            .HasMaxLength(1000);
        builder.Property(a => a.Link)
            .HasMaxLength(500);

        builder.HasIndex(a => new { a.UserId, a.OccurredAtUtc });
        builder.HasIndex(a => a.ActivityType);
    }
}
