using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Contracts.Dtos;
using Ilmi.Modules.UsersIdentity.Contracts.Services;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.Rules;

namespace Ilmi.Modules.UsersIdentity.Application.Services;

/// <summary>Implements the public Contracts for other modules. Other modules never see entities.</summary>
public sealed class UserDirectoryService(IUserRepository users) : IUserDirectory
{
    public async Task<UserSummaryDto?> FindAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, UserIncludes.Rules, ct);
        if (user is null) return null;
        var rules = (await users.GetRulesAsync(ct)).Where(r => user.HasRule(r.Id)).Select(r => r.Code).ToList();
        return new UserSummaryDto(user.Id, user.UserKind.ToString(), user.UserType?.ToString(), user.AccountStatus.ToString(),
            user.IsActive, user.FirstName, user.LastName, user.Email!, rules);
    }

    public async Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default) =>
        await users.GetByIdAsync(userId, UserIncludes.None, ct) is not null;
}

/// <summary>
/// Adult -> Parent and Parent -> Adult, driven by the Family module. Only the Rule changes: UserType stays Adult.
/// Every change is recorded in the append-only Rule history.
/// </summary>
public sealed class UserRuleService(IUserRepository users, IUnitOfWork uow, TimeProvider clock) : IUserRuleService
{
    public Task<RuleTransitionOutcome> MarkAsParentAsync(Guid userId, Guid? relatedFamilyLinkId, Guid? changedBy, CancellationToken ct = default) =>
        TransitionAsync(userId, RuleIds.Adult, RuleIds.Parent, RuleChangeReason.DependentAdded, relatedFamilyLinkId, changedBy, ct);

    public Task<RuleTransitionOutcome> RevertParentToAdultAsync(Guid userId, Guid? relatedFamilyLinkId, Guid? changedBy, CancellationToken ct = default) =>
        TransitionAsync(userId, RuleIds.Parent, RuleIds.Adult, RuleChangeReason.LastDependentUnlinked, relatedFamilyLinkId, changedBy, ct);

    private async Task<RuleTransitionOutcome> TransitionAsync(Guid userId, short from, short to, RuleChangeReason reason,
        Guid? linkId, Guid? changedBy, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, UserIncludes.Rules, ct);
        if (user is null) return RuleTransitionOutcome.UserNotFound;
        if (!user.IsExternal || user.UserType != UserType.Adult) return RuleTransitionOutcome.NotAllowed;
        if (user.HasRule(to)) return RuleTransitionOutcome.NoChange;
        if (!user.HasRule(from)) return RuleTransitionOutcome.NotAllowed;

        var result = user.ReplaceRule(from, to, reason, clock.GetUtcNow().UtcDateTime, changedBy, linkId);
        if (result.IsFailure) return RuleTransitionOutcome.NotAllowed;
        await uow.SaveChangesAsync(ct);
        return RuleTransitionOutcome.Changed;
    }
}

/// <summary>
/// The Parent Add Dependent lockout. The duration (10 minutes) is confirmed; the number of failed attempts that
/// triggers it is an OPEN decision, so without a configured threshold failures are counted but never lock.
/// </summary>
public sealed class AddDependentLockoutService(IOtpRepository otps, IUnitOfWork uow, TimeProvider clock, IdentitySecurityOptions options) : IAddDependentLockout
{
    private const OtpPurpose Purpose = OtpPurpose.DependentVerification;
    private static string Scope(Guid parentUserId) => parentUserId.ToString("N");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<LockoutStatusDto> GetStatusAsync(Guid parentUserId, CancellationToken ct = default) =>
        ToDto(await otps.GetLockoutAsync(Purpose, Scope(parentUserId), ct));

    public async Task<LockoutStatusDto> RegisterFailedAttemptAsync(Guid parentUserId, CancellationToken ct = default)
    {
        var lockout = await otps.GetLockoutAsync(Purpose, Scope(parentUserId), ct);
        if (lockout is null)
        {
            lockout = Domain.Entities.VerificationLockout.Create(Purpose, Scope(parentUserId));
            otps.Add(lockout);
        }
        lockout.RegisterFailure(Now, options.AddDependentMaxFailedAttempts, TimeSpan.FromMinutes(options.AddDependentLockMinutes));
        await uow.SaveChangesAsync(ct);
        return ToDto(lockout);
    }

    public async Task ResetAsync(Guid parentUserId, CancellationToken ct = default)
    {
        var lockout = await otps.GetLockoutAsync(Purpose, Scope(parentUserId), ct);
        if (lockout is null) return;
        lockout.Reset();
        await uow.SaveChangesAsync(ct);
    }

    private LockoutStatusDto ToDto(Domain.Entities.VerificationLockout? l) =>
        new(l?.IsLocked(Now) == true, l?.IsLocked(Now) == true ? l.LockedUntil : null, l?.FailedCount ?? 0, options.AddDependentMaxFailedAttempts is not null);
}
