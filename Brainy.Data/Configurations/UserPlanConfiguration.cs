using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

public class UserPlanConfiguration : IEntityTypeConfiguration<UserPlan>
{
    public void Configure(EntityTypeBuilder<UserPlan> builder)
    {
        builder.ToTable("UserPlan");

        builder.HasKey(p => p.Id);

        builder.ConfigureUserOwnership();
        builder.HasIndex(p => p.UserId).IsUnique();

        builder.Property(p => p.Tier)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.PlanTier.Starter);

        builder.Property(p => p.BillingProviderCustomerId)
            .HasMaxLength(200);

        builder.Property(p => p.BillingProviderSubscriptionId)
            .HasMaxLength(200);

        builder.Property(p => p.AiAllowanceUsedInPeriod)
            .HasDefaultValue(0);
    }
}
