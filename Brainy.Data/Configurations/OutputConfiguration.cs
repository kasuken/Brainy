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
    }
}
