using Brainy.Data.Identity;
using Brainy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Brainy.Data.Configurations;

/// <summary>
/// Shared EF Core configuration for principal entities owned by a user.
/// Maps <see cref="IUserOwnedEntity.UserId"/> as a required, indexed foreign key to
/// the Identity user table, enforcing that all principal data belongs to a user.
/// </summary>
internal static class UserOwnedEntityConfiguration
{
    public static void ConfigureUserOwnership<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IUserOwnedEntity
    {
        builder.Property(e => e.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(e => e.UserId);

        // Restrict on delete to avoid multiple cascade paths in SQL Server; a user's
        // data must be removed explicitly before the user can be deleted.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
