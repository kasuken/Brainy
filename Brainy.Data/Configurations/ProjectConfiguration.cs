using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Project");

        builder.HasKey(p => p.Id);

        builder.ConfigureUserOwnership();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Emoji)
            .HasMaxLength(16);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.DesiredOutcome)
            .HasMaxLength(1000);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Priority)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.HasIndex(p => p.IsArchived);
        builder.HasIndex(p => new { p.UserId, p.Status });

        builder.HasMany(p => p.Tasks)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Notes)
            .WithOne(n => n.Project)
            .HasForeignKey(n => n.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Outputs)
            .WithOne(o => o.Project)
            .HasForeignKey(o => o.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
