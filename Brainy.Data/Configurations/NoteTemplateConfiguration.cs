using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class NoteTemplateConfiguration : IEntityTypeConfiguration<NoteTemplate>
{
    public void Configure(EntityTypeBuilder<NoteTemplate> builder)
    {
        builder.ToTable("NoteTemplate");

        builder.HasKey(t => t.Id);

        builder.ConfigureUserOwnership();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.TitlePattern)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.ContentScaffold)
            .IsRequired();

        builder.Property(t => t.DefaultParaCategory)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(t => t.UserId);
    }
}
