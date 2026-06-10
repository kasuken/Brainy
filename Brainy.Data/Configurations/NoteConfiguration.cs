using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Note");

        builder.HasKey(n => n.Id);

        builder.ConfigureUserOwnership();

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(n => n.Content)
            .IsRequired();

        builder.Property(n => n.AiSummary)
            .HasMaxLength(4000);

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(n => n.ParaCategory)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasIndex(n => n.Status);
        builder.HasIndex(n => n.ParaCategory);
        builder.HasIndex(n => new { n.UserId, n.Status });
        builder.HasIndex(n => new { n.UserId, n.IsFavorite });
        // Supports fast title-based search and ordering.
        builder.HasIndex(n => new { n.UserId, n.Title });
        builder.HasIndex(n => new { n.UserId, n.IsArchived });

        builder.HasOne(n => n.Source)
            .WithMany(s => s.Notes)
            .HasForeignKey(n => n.SourceId)
            .OnDelete(DeleteBehavior.SetNull);

        // Note <-> Tag many-to-many via skip navigation.
        builder.HasMany(n => n.Tags)
            .WithMany(t => t.Notes)
            .UsingEntity(j => j.ToTable("NoteTag"));

        // Note <-> Output many-to-many via skip navigation.
        builder.HasMany(n => n.Outputs)
            .WithMany(o => o.SourceNotes)
            .UsingEntity(j => j.ToTable("OutputNote"));
    }
}
