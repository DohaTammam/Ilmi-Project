using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Commands.Users;

// ============================================================ profile (UC007). Email, mobile and date of birth are not editable here:
// the email-change and mobile-change flows are not defined by the requirements.
public sealed record UpdateMyProfileCommand(string FirstName, string LastName, Gender? Gender, short? AvatarId, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds)
    : ICommand<UserProfileDto>;

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Gender).IsInEnum().When(x => x.Gender.HasValue);
    }
}

public sealed class UpdateMyProfileHandler(
    IValidator<UpdateMyProfileCommand> validator, ICurrentUser current, IUserRepository users, IUnitOfWork uow)
    : ICommandHandler<UpdateMyProfileCommand, UserProfileDto>
{
    public async Task<Result<UserProfileDto>> HandleAsync(UpdateMyProfileCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var user = await users.GetByIdAsync(userId, UserIncludes.Rules, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        if (c.AvatarId is { } avatar && !await users.AvatarExistsAsync(avatar, ct)) return IdentityErrors.UnknownAvatar;

        // Internal accounts carry names only; the other fields belong to External users.
        var changed = user.IsExternal
            ? user.UpdateExternalProfile(c.FirstName, c.LastName, c.Gender, c.AvatarId, c.ReceiveNewsletter, c.NotifyIlmiSpecialNeeds)
            : c.Gender is null && c.AvatarId is null && !c.ReceiveNewsletter && !c.NotifyIlmiSpecialNeeds
                ? user.Rename(c.FirstName, c.LastName)
                : Domain.UserErrors.ExternalOnly;
        if (changed.IsFailure) return changed.Error!;
        await uow.SaveChangesAsync(ct);
        return user.ToProfile(await users.GetRulesAsync(ct));
    }
}

// ============================================================ settings: language and email notifications (no accessibility mode)
public sealed record UpdateMySettingsCommand(string Language, bool EmailNotificationsEnabled) : ICommand<UserSettingsDto>;

public sealed class UpdateMySettingsValidator : AbstractValidator<UpdateMySettingsCommand>
{
    public UpdateMySettingsValidator() =>
        RuleFor(x => x.Language).Must(l => Mapping.TryParseLanguage(l, out _)).WithMessage("The language must be 'ar' or 'en'.");
}

public sealed class UpdateMySettingsHandler(
    IValidator<UpdateMySettingsCommand> validator, ICurrentUser current, IUserRepository users, IUnitOfWork uow)
    : ICommandHandler<UpdateMySettingsCommand, UserSettingsDto>
{
    public async Task<Result<UserSettingsDto>> HandleAsync(UpdateMySettingsCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var user = await users.GetByIdAsync(userId, UserIncludes.Settings, ct);
        if (user?.Settings is null) return IdentityErrors.UserNotFound;
        Mapping.TryParseLanguage(c.Language, out var language);
        user.Settings.Update(language, c.EmailNotificationsEnabled);
        await uow.SaveChangesAsync(ct);
        return new UserSettingsDto(user.Settings.Language.ToCode(), user.Settings.EmailNotificationsEnabled);
    }
}

// ============================================================ declared special needs (junction with the DisabilityTypes lookup)
public sealed record ReplaceMyAccessibilityNeedsCommand(List<short> DisabilityTypeIds) : ICommand<IReadOnlyList<DisabilityTypeDto>>;

public sealed class ReplaceMyAccessibilityNeedsHandler(ICurrentUser current, IUserRepository users, IUnitOfWork uow, TimeProvider clock)
    : ICommandHandler<ReplaceMyAccessibilityNeedsCommand, IReadOnlyList<DisabilityTypeDto>>
{
    public async Task<Result<IReadOnlyList<DisabilityTypeDto>>> HandleAsync(ReplaceMyAccessibilityNeedsCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var user = await users.GetByIdAsync(userId, UserIncludes.AccessibilityNeeds, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        var lookup = (await users.GetDisabilityTypesAsync(ct)).Where(t => t.IsActive).ToDictionary(t => t.Id);
        var ids = c.DisabilityTypeIds ?? [];
        if (ids.Any(id => !lookup.ContainsKey(id))) return IdentityErrors.UnknownDisabilityType;
        var replaced = user.ReplaceAccessibilityNeeds(ids, clock.GetUtcNow().UtcDateTime);
        if (replaced.IsFailure) return replaced.Error!;
        await uow.SaveChangesAsync(ct);
        return Result.Success<IReadOnlyList<DisabilityTypeDto>>(ids.Select(id => lookup[id].ToDto()).OrderBy(d => d.SortOrder).ToList());
    }
}

// ============================================================ account lifecycle. These are application-level operations for
// future administrative use; they are NOT exposed over HTTP because the internal authorization model is not defined yet.

/// <summary>PendingActivation -> Active (within the 10-minute window). The only lifecycle activation the requirements define.</summary>
public sealed record ActivateAccountCommand(Guid UserId) : ICommand<Unit>;

public sealed class ActivateAccountHandler(IUserRepository users, IUnitOfWork uow, TimeProvider clock) : ICommandHandler<ActivateAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ActivateAccountCommand c, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(c.UserId, UserIncludes.None, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        var r = user.ActivatePendingAccount(clock.GetUtcNow().UtcDateTime);
        if (r.IsFailure) return r.Error!;
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

/// <summary>Accounts are deactivated, never hard-deleted. Sessions are revoked and Quick Login is disabled.</summary>
public sealed record DeactivateAccountCommand(Guid UserId) : ICommand<Unit>;

public sealed class DeactivateAccountHandler(IUserRepository users, SessionService sessions, QuickLoginDisabler quickLogin, IUnitOfWork uow)
    : ICommandHandler<DeactivateAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateAccountCommand c, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(c.UserId, UserIncludes.None, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        user.Deactivate();
        await sessions.RevokeAllForUserAsync(user.Id, SessionRevokedReason.SystemRevoked, ct);
        await quickLogin.DisableAllForUserAsync(user.Id, QuickLoginDisabledReason.AccountDeactivated, ct);
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

/// <summary>Opens or closes the administrative access gate (IsActive). Independent of AccountStatus.</summary>
public sealed record SetUserAccessCommand(Guid UserId, bool IsActive) : ICommand<Unit>;

public sealed class SetUserAccessHandler(IUserRepository users, IUnitOfWork uow) : ICommandHandler<SetUserAccessCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetUserAccessCommand c, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(c.UserId, UserIncludes.None, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        user.SetAdministrativeAccess(c.IsActive);
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
