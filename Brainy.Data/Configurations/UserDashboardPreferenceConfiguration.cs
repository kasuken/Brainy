using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class UserDashboardPreferenceConfiguration : IEntityTypeConfiguration<UserDashboardPreference>
{
    public void Configure(EntityTypeBuilder<UserDashboardPreference> builder)
    {
        builder.ToTable("UserDashboardPreference");

        builder.HasKey(p => p.Id);

        builder.ConfigureUserOwnership();

        builder.Property(p => p.WidgetOrder)
            .HasMaxLength(2000);

        builder.Property(p => p.CollapsedWidgets)
            .HasMaxLength(2000);
    }
}
