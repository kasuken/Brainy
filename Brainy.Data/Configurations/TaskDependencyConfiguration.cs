using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependency");

        builder.HasKey(d => d.Id);

        // A task can only depend on another task once (unique pair).
        builder.HasIndex(d => new { d.TaskId, d.DependsOnTaskId }).IsUnique();

        builder.HasOne(d => d.Task)
            .WithMany(t => t.Dependencies)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.DependsOnTask)
            .WithMany(t => t.Dependents)
            .HasForeignKey(d => d.DependsOnTaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
