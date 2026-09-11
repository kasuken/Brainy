using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

/// <summary>
/// EF Core mapping for per-user, permanent weekly-review resurfacing dismissals.
/// </summary>
public sealed class ResurfacingDismissalConfiguration : IEntityTypeConfiguration<ResurfacingDismissal>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResurfacingDismissal> builder)
    {
        builder.ToTable("ResurfacingDismissal");

        builder.HasKey(dismissal => dismissal.Id);

        builder.ConfigureUserOwnership();

        // One dismissal per user per note: a repeat dismissal is a no-op, not a new row.
        builder.HasIndex(dismissal => new { dismissal.UserId, dismissal.NoteId })
            .IsUnique();

        builder.HasOne(dismissal => dismissal.Note)
            .WithMany()
            .HasForeignKey(dismissal => dismissal.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
