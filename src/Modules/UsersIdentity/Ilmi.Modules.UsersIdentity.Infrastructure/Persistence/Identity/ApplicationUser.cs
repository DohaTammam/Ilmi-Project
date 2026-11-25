using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;

/// <summary>
/// The ASP.NET Core Identity user. It lives in Infrastructure; the Domain knows nothing about it.
/// It maps to the Identity users table <c>users.AspNetUsers</c> and shares that row with the Domain <see cref="User"/>
/// (EF Core table splitting, same primary key): this entity owns Identity's columns (user name, normalized email, password hash,
/// security stamp, lockout, ...), the Domain User owns the business columns (name, kind, status, mobile, ...). Email and normalized email
/// are the only columns both map; they always carry the same value.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The Domain user stored in the same AspNetUsers row.</summary>
    public User Account { get; set; } = null!;

    /// <summary>The Identity side of a Domain user: the single place where the Domain and Identity models are mapped.</summary>
    public static ApplicationUser From(User user) => new()
    {
        Account = user,
        Id = user.Id,
        UserName = user.Email,                       // the user name is the email address (login is by email only)
        NormalizedUserName = user.NormalizedEmail,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        LockoutEnabled = true
    };
}

/// <summary>
/// An Identity security role (AspNetRoles). Security roles and permissions are separate from the business Rules
/// (CHILD, TEEN, ADULT, PARENT, REGISTRATION_OFFICER). The requirements define no roles, so none are seeded.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}
