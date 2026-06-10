using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class IdeaConfiguration : IEntityTypeConfiguration<Idea>
{
    public void Configure(EntityTypeBuilder<Idea> builder)
    {
        builder.ToTable("Idea");

        builder.HasKey(i => i.Id);

        builder.ConfigureUserOwnership();

        builder.Property(i => i.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(i => i.Description)
            .HasMaxLength(2000);

        // Research, Competitors, Notes are nvarchar(max) — no length cap needed.
        builder.Property(i => i.Research);
        builder.Property(i => i.Competitors);
        builder.Property(i => i.Notes);

        builder.HasIndex(i => i.Title);

        builder.HasOne(i => i.Area)
            .WithMany(a => a.Ideas)
            .HasForeignKey(i => i.AreaId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
