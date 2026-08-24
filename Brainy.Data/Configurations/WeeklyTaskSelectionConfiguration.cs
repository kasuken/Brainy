using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

/// <summary>
/// EF Core mapping for explicit per-user weekly task commitments.
/// </summary>
public sealed class WeeklyTaskSelectionConfiguration : IEntityTypeConfiguration<WeeklyTaskSelection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WeeklyTaskSelection> builder)
    {
        builder.ToTable("WeeklyTaskSelection");

        builder.HasKey(selection => selection.Id);

        builder.ConfigureUserOwnership();

        builder.Property(selection => selection.WeekStartDate)
            .HasColumnType("date");

        builder.HasIndex(selection => new { selection.UserId, selection.WeekStartDate, selection.TaskId })
            .IsUnique();

        builder.HasIndex(selection => new { selection.UserId, selection.WeekStartDate });
        builder.HasIndex(selection => new { selection.UserId, selection.TaskId });

        builder.HasOne(selection => selection.Task)
            .WithMany()
            .HasForeignKey(selection => selection.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
