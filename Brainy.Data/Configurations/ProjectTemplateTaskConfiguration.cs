using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ProjectTemplateTaskConfiguration : IEntityTypeConfiguration<ProjectTemplateTask>
{
    public void Configure(EntityTypeBuilder<ProjectTemplateTask> builder)
    {
        builder.ToTable("ProjectTemplateTask");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(t => t.Description)
            .HasMaxLength(4000);

        builder.Property(t => t.Priority)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(t => t.Complexity)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.HasIndex(t => t.ProjectTemplateId);
    }
}
