using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class NoteImageConfiguration : IEntityTypeConfiguration<NoteImage>
{
    public void Configure(EntityTypeBuilder<NoteImage> builder)
    {
        builder.ToTable("NoteImage");

        builder.HasKey(i => i.Id);

        builder.ConfigureUserOwnership();

        builder.Property(i => i.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(i => i.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.Data)
            .IsRequired()
            .HasColumnType("varbinary(max)");

        builder.HasIndex(i => i.NoteId);

        // Deleting a note removes its embedded images.
        builder.HasOne(i => i.Note)
            .WithMany(n => n.Images)
            .HasForeignKey(i => i.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
