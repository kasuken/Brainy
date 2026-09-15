using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class OutputTemplateConfiguration : IEntityTypeConfiguration<OutputTemplate>
{
    public void Configure(EntityTypeBuilder<OutputTemplate> builder)
    {
        builder.ToTable("OutputTemplate");

        builder.HasKey(t => t.Id);

        builder.ConfigureUserOwnership();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.TitlePattern)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Type)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(t => t.ContentScaffold)
            .IsRequired();

        builder.Property(t => t.DefaultSourceSelection)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasIndex(t => t.UserId);
    }
}
