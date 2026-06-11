using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class OutputConfiguration : IEntityTypeConfiguration<Output>
{
    public void Configure(EntityTypeBuilder<Output> builder)
    {
        builder.ToTable("Output");

        builder.HasKey(o => o.Id);

        builder.ConfigureUserOwnership();

        builder.Property(o => o.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Content)
            .IsRequired();

        builder.Property(o => o.Type)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(o => o.Model)
            .HasMaxLength(100);

        builder.Property(o => o.PromptVersion)
            .HasMaxLength(100);

        builder.Property(o => o.Description)
            .HasMaxLength(2000);

        builder.Property(o => o.IsArchived);

        builder.Property(o => o.PublishedDate);

        builder.Property(o => o.ArchivedDate);

        builder.HasOne(o => o.Area)
            .WithMany()
            .HasForeignKey(o => o.AreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.Goal)
            .WithMany()
            .HasForeignKey(o => o.GoalId)
            .OnDelete(DeleteBehavior.SetNull);

    }
}
