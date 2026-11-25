using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Commands.Identity;

// ============================================================ forgot password (UC005): request a code
public sealed record RequestPasswordResetCommand(string Email) : ICommand<Unit>;

public sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator() => RuleFor(x => x.Email).Must(EmailAddress.IsValid).WithMessage("A valid email address is required.");
}

/// <summary>Always answers success, so the endpoint cannot be used to discover which emails are registered.</summary>
public sealed class RequestPasswordResetHandler(
    IValidator<RequestPasswordResetCommand> validator, IUserRepository users, IUserAccountManager accounts, OtpService otp)
    : ICommandHandler<RequestPasswordResetCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RequestPasswordResetCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var user = await users.GetByNormalizedEmailAsync(EmailAddress.Normalize(c.Email), UserIncludes.None, ct);
        if (user is { CanSignIn: true } && await accounts.HasPasswordAsync(user, ct))
        {
            var issued = await otp.IssueAsync(OtpPurpose.PasswordReset, user.Email!, user.Id, null, null, ct);
            if (issued.IsFailure && issued.Error!.Type == ErrorType.Locked) return issued.Error!;
        }
        return Unit.Value;
    }
}

// ============================================================ forgot password: code + new password
public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword) : ICommand<Unit>;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).Must(EmailAddress.IsValid);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(256);
    }
}

public sealed class ResetPasswordHandler(
    IValidator<ResetPasswordCommand> validator, OtpService otp, IUserRepository users, IUserAccountManager accounts,
    PasswordPolicy policy, QuickLoginDisabler quickLogin, IUnitOfWork uow)
    : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResetPasswordCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        if (!policy.IsSatisfiedBy(c.NewPassword)) return IdentityErrors.PasswordPolicy;

        var verified = await otp.VerifyAsync(OtpPurpose.PasswordReset, c.Email, c.Code, ct);
        if (verified.IsFailure) return verified.Error!;
        var user = verified.Value.UserId is { } id ? await users.GetByIdAsync(id, UserIncludes.None, ct) : null;
        if (user is null) return IdentityErrors.OtpInvalid;
        if (!await accounts.HasPasswordAsync(user, ct)) return IdentityErrors.PasswordNotApplicable;     // social-only accounts have no password
        if (await accounts.CheckPasswordAsync(user, c.NewPassword, ct)) return IdentityErrors.PasswordSameAsCurrent;

        await quickLogin.DisableAllForUserAsync(user.Id, QuickLoginDisabledReason.PasswordChanged, ct);   // password change disables Quick Login
        var set = await accounts.SetPasswordAsync(user, c.NewPassword, ct);
        if (set.IsFailure) return set.Error!;
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ============================================================ change password while signed in (UC008)
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(256);
    }
}

public sealed class ChangePasswordHandler(
    IValidator<ChangePasswordCommand> validator, ICurrentUser current, IUserRepository users, IUserAccountManager accounts,
    PasswordPolicy policy, QuickLoginDisabler quickLogin, IUnitOfWork uow) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangePasswordCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var user = await users.GetByIdAsync(userId, UserIncludes.None, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        if (!await accounts.HasPasswordAsync(user, ct)) return IdentityErrors.PasswordNotApplicable;     // Google/Apple accounts cannot create or use a password
        if (!await accounts.CheckPasswordAsync(user, c.CurrentPassword, ct)) return IdentityErrors.InvalidCredentials;
        if (!policy.IsSatisfiedBy(c.NewPassword)) return IdentityErrors.PasswordPolicy;
        if (await accounts.CheckPasswordAsync(user, c.NewPassword, ct)) return IdentityErrors.PasswordSameAsCurrent;

        await quickLogin.DisableAllForUserAsync(userId, QuickLoginDisabledReason.PasswordChanged, ct);
        var set = await accounts.SetPasswordAsync(user, c.NewPassword, ct);
        if (set.IsFailure) return set.Error!;
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
