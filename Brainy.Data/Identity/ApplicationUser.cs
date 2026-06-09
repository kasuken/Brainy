using Microsoft.AspNetCore.Identity;

namespace Brainy.Data.Identity;

/// <summary>
/// The application user. Extend this type to add profile data for users.
/// All principal Brainy entities reference a user via their UserId.
/// </summary>
public class ApplicationUser : IdentityUser
{
}
