using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class HighlightConfiguration : IEntityTypeConfiguration<Highlight>
{
    public void Configure(EntityTypeBuilder<Highlight> builder)
    {
        builder.ToTable("Highlight");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Text)
            .IsRequired();

        builder.Property(h => h.Annotation)
            .HasMaxLength(2000);

        builder.HasOne(h => h.Note)
            .WithMany(n => n.Highlights)
            .HasForeignKey(h => h.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
