using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class NoteRevisionConfiguration : IEntityTypeConfiguration<NoteRevision>
{
    public void Configure(EntityTypeBuilder<NoteRevision> builder)
    {
        builder.ToTable("NoteRevision");

        builder.HasKey(r => r.Id);

        builder.ConfigureUserOwnership();

        builder.Property(r => r.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(r => r.Content)
            .IsRequired();

        builder.Property(r => r.Model)
            .HasMaxLength(200);

        builder.Property(r => r.PromptVersion)
            .HasMaxLength(100);

        builder.Property(r => r.Reason)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Supports the timeline query (a note's revisions ordered by capture time) and
        // retention purges (oldest-first per note).
        builder.HasIndex(r => new { r.NoteId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.UserId, r.NoteId });

        // Deleting a note removes its revision history with it.
        builder.HasOne(r => r.Note)
            .WithMany(n => n.Revisions)
            .HasForeignKey(r => r.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referencing "restored from" pointer. SQL Server refuses a cascading (or
        // SET NULL) action here because NoteRevision already cascades from Note, which
        // would create a second, ambiguous cascade path into the same table. Restrict
        // (NO ACTION at the database level) avoids that; application code is responsible
        // for clearing this pointer before an old revision it targets is purged by
        // retention (see NoteRevisionSupport.TrimRetentionAsync).
        builder.HasOne<NoteRevision>()
            .WithMany()
            .HasForeignKey(r => r.RestoredFromRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
