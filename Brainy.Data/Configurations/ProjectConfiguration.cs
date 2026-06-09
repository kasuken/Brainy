using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Project");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.HasIndex(p => p.IsArchived);

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
