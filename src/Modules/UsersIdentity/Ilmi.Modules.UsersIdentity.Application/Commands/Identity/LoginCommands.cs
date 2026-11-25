using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Commands.Identity;

// ============================================================ step 1: email + password -> email OTP (UC003)
public sealed record LoginWithPasswordCommand(string Email, string Password) : ICommand<OtpRequestedDto>;

public sealed class LoginWithPasswordValidator : AbstractValidator<LoginWithPasswordCommand>
{
    public LoginWithPasswordValidator()
    {
        RuleFor(x => x.Email).Must(EmailAddress.IsValid).WithMessage("A valid email address is required.");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginWithPasswordHandler(
    IValidator<LoginWithPasswordCommand> validator, IUserRepository users, IUserAccountManager accounts,
    OtpService otp, ILoginAuditRepository audit, IUnitOfWork uow, TimeProvider clock)
    : ICommandHandler<LoginWithPasswordCommand, OtpRequestedDto>
{
    public async Task<Result<OtpRequestedDto>> HandleAsync(LoginWithPasswordCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.GetByNormalizedEmailAsync(EmailAddress.Normalize(c.Email), UserIncludes.None, ct);
        // A social-only account has no password hash: it gets the same answer as a wrong password.
        if (user is null || !await accounts.HasPasswordAsync(user, ct))
        {
            audit.Add(LoginAudit.Record(user?.Id, LoginMethod.PasswordOtp, LoginOutcome.InvalidCredentials, null, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.InvalidCredentials;
        }
        if (await accounts.IsLockedOutAsync(user, ct))
        {
            audit.Add(LoginAudit.Record(user.Id, LoginMethod.PasswordOtp, LoginOutcome.AccountLocked, null, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.AccountLocked;
        }
        if (!await accounts.CheckPasswordAsync(user, c.Password, ct))
        {
            var locked = await accounts.RegisterFailedPasswordAsync(user, ct);
            audit.Add(LoginAudit.Record(user.Id, LoginMethod.PasswordOtp, locked ? LoginOutcome.AccountLocked : LoginOutcome.InvalidCredentials, null, now));
            await uow.SaveChangesAsync(ct);
            return locked ? IdentityErrors.AccountLocked : IdentityErrors.InvalidCredentials;
        }
        if (!user.CanSignIn)
        {
            audit.Add(LoginAudit.Record(user.Id, LoginMethod.PasswordOtp, LoginOutcome.AccountInactive, null, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.AccountInactive;
        }

        await accounts.ResetFailedPasswordsAsync(user, ct);
        var issued = await otp.IssueAsync(OtpPurpose.Login, user.Email!, user.Id, null, null, ct);
        return issued.IsFailure ? issued.Error! : new OtpRequestedDto(issued.Value.ExpiresAt, issued.Value.ResendsRemaining);
    }
}

// ============================================================ step 2: email OTP -> session
public sealed record VerifyLoginOtpCommand(string Email, string Code, ClientType ClientType, string? DeviceId) : ICommand<SessionDto>;

public sealed class VerifyLoginOtpValidator : AbstractValidator<VerifyLoginOtpCommand>
{
    public VerifyLoginOtpValidator()
    {
        RuleFor(x => x.Email).Must(EmailAddress.IsValid);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.ClientType).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(100);
    }
}

public sealed class VerifyLoginOtpHandler(
    IValidator<VerifyLoginOtpCommand> validator, OtpService otp, IUserRepository users, SessionService sessions, QuickLoginDisabler quickLogin,
    ILoginAuditRepository audit, IUnitOfWork uow, TimeProvider clock) : ICommandHandler<VerifyLoginOtpCommand, SessionDto>
{
    public async Task<Result<SessionDto>> HandleAsync(VerifyLoginOtpCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var now = clock.GetUtcNow().UtcDateTime;
        var verified = await otp.VerifyAsync(OtpPurpose.Login, c.Email, c.Code, ct);
        if (verified.IsFailure)
        {
            var known = await users.GetByNormalizedEmailAsync(EmailAddress.Normalize(c.Email), UserIncludes.None, ct);
            audit.Add(LoginAudit.Record(known?.Id, LoginMethod.PasswordOtp,
                verified.Error!.Type == ErrorType.Locked ? LoginOutcome.AccountLocked : LoginOutcome.InvalidOtp, c.DeviceId, now));
            await uow.SaveChangesAsync(ct);
            return verified.Error!;
        }

        var user = verified.Value.UserId is { } id ? await users.GetByIdAsync(id, UserIncludes.None, ct) : null;
        if (user is null) return IdentityErrors.OtpInvalid;
        if (!user.CanSignIn)
        {
            audit.Add(LoginAudit.Record(user.Id, LoginMethod.PasswordOtp, LoginOutcome.AccountInactive, c.DeviceId, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.AccountInactive;
        }

        var session = sessions.Create(user.Id, c.ClientType, c.DeviceId);
        audit.Add(LoginAudit.Record(user.Id, LoginMethod.PasswordOtp, LoginOutcome.Success, c.DeviceId, now));
        if (c.DeviceId is not null) await quickLogin.DisableOtherUsersOnDeviceAsync(c.DeviceId, user.Id, ct);
        await uow.SaveChangesAsync(ct);
        return new SessionDto(session.SessionId, session.UserId, session.Token, session.IssuedAt, session.ExpiresAt);
    }
}

// ============================================================ Google / Apple sign-in: no OTP (UC003 ALT001)
public sealed record LoginWithExternalProviderCommand(ExternalProvider Provider, string IdToken, ClientType ClientType, string? DeviceId) : ICommand<SessionDto>;

public sealed class LoginWithExternalProviderValidator : AbstractValidator<LoginWithExternalProviderCommand>
{
    public LoginWithExternalProviderValidator()
    {
        RuleFor(x => x.Provider).IsInEnum();
        RuleFor(x => x.IdToken).NotEmpty();
        RuleFor(x => x.ClientType).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(100);
    }
}

public sealed class LoginWithExternalProviderHandler(
    IValidator<LoginWithExternalProviderCommand> validator, IExternalIdentityVerifier verifier, IUserAccountManager accounts,
    SessionService sessions, QuickLoginDisabler quickLogin, ILoginAuditRepository audit, IUnitOfWork uow, TimeProvider clock)
    : ICommandHandler<LoginWithExternalProviderCommand, SessionDto>
{
    public async Task<Result<SessionDto>> HandleAsync(LoginWithExternalProviderCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var now = clock.GetUtcNow().UtcDateTime;
        var method = c.Provider == ExternalProvider.Google ? LoginMethod.Google : LoginMethod.Apple;
        var identity = await verifier.VerifyAsync(c.Provider, c.IdToken, ct);
        if (identity is null) return IdentityErrors.ExternalTokenInvalid;
        var user = await accounts.FindByExternalLoginAsync(c.Provider, identity.Subject, ct);
        if (user is null) return IdentityErrors.ExternalLoginNotRegistered;
        if (!user.CanSignIn)
        {
            audit.Add(LoginAudit.Record(user.Id, method, LoginOutcome.AccountInactive, c.DeviceId, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.AccountInactive;
        }
        var session = sessions.Create(user.Id, c.ClientType, c.DeviceId);
        audit.Add(LoginAudit.Record(user.Id, method, LoginOutcome.Success, c.DeviceId, now));
        if (c.DeviceId is not null) await quickLogin.DisableOtherUsersOnDeviceAsync(c.DeviceId, user.Id, ct);
        await uow.SaveChangesAsync(ct);
        return new SessionDto(session.SessionId, session.UserId, session.Token, session.IssuedAt, session.ExpiresAt);
    }
}

// ============================================================ resend OTP (UC005: limited resends, same challenge)
public sealed record ResendOtpCommand(OtpPurpose Purpose, string Email) : ICommand<OtpRequestedDto>;

public sealed class ResendOtpValidator : AbstractValidator<ResendOtpCommand>
{
    public ResendOtpValidator()
    {
        RuleFor(x => x.Email).Must(EmailAddress.IsValid);
        RuleFor(x => x.Purpose).Must(p => p is OtpPurpose.Registration or OtpPurpose.Login or OtpPurpose.PasswordReset)
            .WithMessage("The OTP purpose cannot be resent from here.");
    }
}

public sealed class ResendOtpHandler(IValidator<ResendOtpCommand> validator, OtpService otp) : ICommandHandler<ResendOtpCommand, OtpRequestedDto>
{
    public async Task<Result<OtpRequestedDto>> HandleAsync(ResendOtpCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var r = await otp.ResendAsync(c.Purpose, c.Email, ct);
        return r.IsFailure ? r.Error! : new OtpRequestedDto(r.Value.ExpiresAt, r.Value.ResendsRemaining);
    }
}
