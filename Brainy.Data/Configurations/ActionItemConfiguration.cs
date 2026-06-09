using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class ActionItemConfiguration : IEntityTypeConfiguration<ActionItem>
{
    public void Configure(EntityTypeBuilder<ActionItem> builder)
    {
        builder.ToTable("ActionItem");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

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
    }
}
