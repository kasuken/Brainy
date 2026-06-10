using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class GoalMilestoneConfiguration : IEntityTypeConfiguration<GoalMilestone>
{
    public void Configure(EntityTypeBuilder<GoalMilestone> builder)
    {
        builder.ToTable("GoalMilestone");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title)
            .IsRequired()
            .HasMaxLength(200);
    }
}
