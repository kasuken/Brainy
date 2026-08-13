using Brainy.Domain.Entities;
using Brainy.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ActionItemConfiguration : IEntityTypeConfiguration<ActionItem>
{
    public void Configure(EntityTypeBuilder<ActionItem> builder)
    {
        builder.ToTable("ActionItem");

        builder.HasKey(a => a.Id);

        // Legacy action items were owned indirectly through Note/Task. The explicit
        // scalar makes every service query tenant-safe; the migration backfills it.
        builder.Property(a => a.UserId)
            // Legacy rows may have become orphaned after both their note and promoted
            // task were deleted. They remain preserved but ownerless and inaccessible.
            .IsRequired(false)
            .HasMaxLength(450);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.Model)
            .HasMaxLength(200);

        builder.Property(a => a.PromptVersion)
            .HasMaxLength(100);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasOne(a => a.Note)
            .WithMany(n => n.ActionItems)
            .HasForeignKey(a => a.NoteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.TaskItem)
            .WithMany()
            .HasForeignKey(a => a.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.UserId, a.NoteId });
    }
}
