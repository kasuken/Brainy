using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class SummaryConfiguration : IEntityTypeConfiguration<Summary>
{
    public void Configure(EntityTypeBuilder<Summary> builder)
    {
        builder.ToTable("Summary");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Content)
            .IsRequired();

        builder.Property(s => s.Model)
            .HasMaxLength(100);

        builder.Property(s => s.PromptVersion)
            .HasMaxLength(100);

        builder.HasOne(s => s.Note)
            .WithMany(n => n.Summaries)
            .HasForeignKey(s => s.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
