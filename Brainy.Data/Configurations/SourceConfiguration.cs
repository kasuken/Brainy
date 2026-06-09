using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Source");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Type)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.Title)
            .HasMaxLength(500);

        builder.Property(s => s.Url)
            .HasMaxLength(2048);

        builder.Property(s => s.Author)
            .HasMaxLength(200);

        builder.Property(s => s.Reference)
            .HasMaxLength(1000);
    }
}
