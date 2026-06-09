using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class NoteRelationshipConfiguration : IEntityTypeConfiguration<NoteRelationship>
{
    public void Configure(EntityTypeBuilder<NoteRelationship> builder)
    {
        builder.ToTable("NoteRelationship");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.Annotation)
            .HasMaxLength(1000);

        builder.HasIndex(r => new { r.SourceNoteId, r.TargetNoteId, r.Type }).IsUnique();

        builder.HasOne(r => r.SourceNote)
            .WithMany(n => n.OutgoingRelationships)
            .HasForeignKey(r => r.SourceNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetNote)
            .WithMany(n => n.IncomingRelationships)
            .HasForeignKey(r => r.TargetNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
