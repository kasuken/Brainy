namespace Brainy.Domain.Common;

/// <summary>
/// Marks a principal entity as owned by a specific application user. The
/// <see cref="UserId"/> holds the identity key of the owning user, ensuring all
/// principal data is scoped to (and isolated per) the logged-in user.
/// </summary>
public interface IUserOwnedEntity
{
    /// <summary>Identity key of the user who owns this entity.</summary>
    string UserId { get; set; }
}
