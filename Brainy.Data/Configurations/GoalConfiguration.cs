using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("Goal");

        builder.HasKey(g => g.Id);

        builder.ConfigureUserOwnership();

        builder.Property(g => g.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(g => g.Description)
            .HasMaxLength(2000);

        builder.Property(g => g.ArchivedReason)
            .HasMaxLength(2000);

        builder.Property(g => g.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(g => g.IsArchived);
        builder.HasIndex(g => new { g.UserId, g.Status });

        builder.HasOne(g => g.Area)
            .WithMany(a => a.Goals)
            .HasForeignKey(g => g.AreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(g => g.Milestones)
            .WithOne(m => m.Goal)
            .HasForeignKey(m => m.GoalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.Projects)
            .WithOne(p => p.Goal)
            .HasForeignKey(p => p.GoalId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
