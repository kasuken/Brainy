using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ProjectTemplateConfiguration : IEntityTypeConfiguration<ProjectTemplate>
{
    public void Configure(EntityTypeBuilder<ProjectTemplate> builder)
    {
        builder.ToTable("ProjectTemplate");

        builder.HasKey(t => t.Id);

        builder.ConfigureUserOwnership();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.ProjectNamePattern)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        builder.Property(t => t.DesiredOutcome)
            .HasMaxLength(1000);

        builder.Property(t => t.DefaultPriority)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.HasIndex(t => t.UserId);

        builder.HasOne(t => t.DefaultArea)
            .WithMany()
            .HasForeignKey(t => t.DefaultAreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.DefaultGoal)
            .WithMany()
            .HasForeignKey(t => t.DefaultGoalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Tasks)
            .WithOne(task => task.ProjectTemplate)
            .HasForeignKey(task => task.ProjectTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
