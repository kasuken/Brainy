using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class GoalActivityConfiguration : IEntityTypeConfiguration<GoalActivity>
{
    public void Configure(EntityTypeBuilder<GoalActivity> builder)
    {
        builder.ToTable("GoalActivity");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActivityType)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.OldValue).HasMaxLength(500);
        builder.Property(a => a.NewValue).HasMaxLength(500);

        builder.HasIndex(a => a.GoalId);
        builder.HasIndex(a => a.CreatedAtUtc);

        // Activities are deleted when their parent Goal is deleted.
        builder.HasOne(a => a.Goal)
            .WithMany()
            .HasForeignKey(a => a.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
