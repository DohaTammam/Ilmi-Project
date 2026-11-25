using System.Text.RegularExpressions;
using Ilmi.Modules.UsersIdentity.Application;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Ilmi.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.ExternalServices;

/// <summary>
/// The Identity integration behind <see cref="IUserAccountManager"/>. This is the only place that touches ASP.NET Core Identity:
/// <see cref="UserManager{TUser}"/> creates users, hashes and checks passwords and manages external logins (AspNetUserLogins), working on
/// the <see cref="ApplicationUser"/> that shares the AspNetUsers row with the Domain <see cref="User"/>. Lockout uses Identity's columns
/// (LockoutEnd, AccessFailedCount, LockoutEnabled) but is evaluated with the injected <see cref="TimeProvider"/>, so it is testable and
/// the limits (5 failures, 10 minutes) come from configuration.
/// </summary>
public sealed partial class UserAccountManager(
    UserManager<ApplicationUser> userManager, UsersIdentityDbContext db, TimeProvider clock, IdentitySecurityOptions options) : IUserAccountManager
{
    /// <summary>Maps a Domain user to its Identity user (same primary key, same AspNetUsers row).</summary>
    private async Task<ApplicationUser> IdentityOf(User user) =>
        await userManager.FindByIdAsync(user.Id.ToString())
        ?? throw new InvalidOperationException($"No Identity user exists for user {user.Id}.");

    public async Task<Result> CreateAsync(User user, string? password, CancellationToken ct)
    {
        var identity = ApplicationUser.From(user);       // carries the Domain user (and its profile, settings and Rules) into the same row
        try
        {
            var result = password is null ? await userManager.CreateAsync(identity) : await userManager.CreateAsync(identity, password);
            if (result.Succeeded) return Result.Success();
            Discard();
            return Map(result);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 } sql)
        {
            // A concurrent registration won the race against the pre-checks: the unique index decided.
            Discard();
            var name = ConstraintName().Match(sql.Message);
            return ExternalUserProvisioner.MapUniqueViolation(new UniqueConstraintViolationException(name.Success ? name.Groups[1].Value : sql.Message));
        }
    }

    /// <summary>Stops tracking what a failed creation added, so a later save cannot insert half an account.</summary>
    private void Discard()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList()) entry.State = EntityState.Detached;
    }

    public async Task<Result> AddExternalLoginAsync(User user, ExternalProvider provider, string subject, CancellationToken ct)
    {
        var result = await userManager.AddLoginAsync(await IdentityOf(user), new UserLoginInfo(provider.ToString(), subject, provider.ToString()));
        return result.Succeeded ? Result.Success() : IdentityErrors.ExternalAlreadyRegistered;
    }

    public async Task<User?> FindByExternalLoginAsync(ExternalProvider provider, string subject, CancellationToken ct)
    {
        var identity = await userManager.FindByLoginAsync(provider.ToString(), subject);
        return identity is null ? null : await db.Accounts.FirstOrDefaultAsync(u => u.Id == identity.Id, ct);
    }

    public async Task<bool> HasPasswordAsync(User user, CancellationToken ct) => await userManager.HasPasswordAsync(await IdentityOf(user));

    public async Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct) =>
        await userManager.CheckPasswordAsync(await IdentityOf(user), password);

    public async Task<bool> IsLockedOutAsync(User user, CancellationToken ct)
    {
        var identity = await IdentityOf(user);
        return identity.LockoutEnabled && identity.LockoutEnd is { } end && end > clock.GetUtcNow();
    }

    public async Task<bool> RegisterFailedPasswordAsync(User user, CancellationToken ct)
    {
        var identity = await IdentityOf(user);
        var now = clock.GetUtcNow();
        if (identity.LockoutEnd is { } end && end <= now) { identity.LockoutEnd = null; identity.AccessFailedCount = 0; }
        identity.AccessFailedCount++;
        var locked = identity.LockoutEnabled && identity.AccessFailedCount >= options.LoginMaxFailures;
        if (locked)
        {
            identity.LockoutEnd = now.AddMinutes(options.LoginLockMinutes);
            identity.AccessFailedCount = 0;
        }
        await userManager.UpdateAsync(identity);
        return locked;
    }

    public async Task ResetFailedPasswordsAsync(User user, CancellationToken ct)
    {
        var identity = await IdentityOf(user);
        if (identity.AccessFailedCount == 0 && identity.LockoutEnd is null) return;
        identity.AccessFailedCount = 0;
        identity.LockoutEnd = null;
        await userManager.UpdateAsync(identity);
    }

    public async Task<Result> SetPasswordAsync(User user, string newPassword, CancellationToken ct)
    {
        var identity = await IdentityOf(user);
        if (await userManager.HasPasswordAsync(identity))
        {
            var removed = await userManager.RemovePasswordAsync(identity);
            if (!removed.Succeeded) return Map(removed);
        }
        var added = await userManager.AddPasswordAsync(identity, newPassword);
        if (!added.Succeeded) return Map(added);
        await ResetFailedPasswordsAsync(user, ct);
        return Result.Success();
    }

    private static Result Map(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToList();
        if (codes.Any(c => c is "DuplicateEmail" or "DuplicateUserName")) return IdentityErrors.EmailTaken;
        if (codes.Any(c => c.StartsWith("Password", StringComparison.Ordinal))) return IdentityErrors.PasswordPolicy;
        var first = result.Errors.First();
        return Error.Failure($"identity.{first.Code}", first.Description);
    }

    [GeneratedRegex(@"(?:unique index|constraint) '([^']+)'")]
    private static partial Regex ConstraintName();
}
