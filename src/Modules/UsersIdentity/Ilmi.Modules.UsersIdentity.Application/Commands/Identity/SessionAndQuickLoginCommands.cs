using System.Text;
using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Commands.Identity;

// ============================================================ logout: revoke the current session
public sealed record LogoutCommand : ICommand<Unit>;

public sealed class LogoutHandler(ICurrentUser current, SessionService sessions, IUnitOfWork uow) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogoutCommand c, CancellationToken ct)
    {
        if (current.SessionId is not { } sessionId) return IdentityErrors.Unauthenticated;
        var revoked = await sessions.RevokeAsync(sessionId, SessionRevokedReason.UserLogout, ct);
        if (revoked.IsFailure) return revoked.Error!;
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ============================================================ Quick Login (UC004): device-bound key pair, mobile only
// The Device PIN is verified by the device operating system. Nothing in this flow receives, carries or stores a PIN.

public sealed record RegisterQuickLoginCommand(string DeviceId, DevicePlatform Platform, string PublicKey, string KeyAlgorithm = KeyAlgorithms.Es256)
    : ICommand<QuickLoginRegisteredDto>;

public sealed class RegisterQuickLoginValidator : AbstractValidator<RegisterQuickLoginCommand>
{
    public RegisterQuickLoginValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.KeyAlgorithm).Equal(KeyAlgorithms.Es256).WithMessage("Only ES256 (ECDSA P-256) is supported.");
        RuleFor(x => x.PublicKey).NotEmpty().MaximumLength(1024).WithMessage("The public key must be Base64 (max 1024 characters).");
    }
}

public sealed class RegisterQuickLoginHandler(
    IValidator<RegisterQuickLoginCommand> validator, ICurrentUser current, IUserRepository users, IQuickLoginRepository quickLogins,
    IQuickLoginCrypto crypto, IUnitOfWork uow, TimeProvider clock) : ICommandHandler<RegisterQuickLoginCommand, QuickLoginRegisteredDto>
{
    public async Task<Result<QuickLoginRegisteredDto>> HandleAsync(RegisterQuickLoginCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var user = await users.GetByIdAsync(userId, UserIncludes.None, ct);
        if (user is null || !user.CanSignIn) return IdentityErrors.AccountInactive;

        byte[] key;
        try { key = Convert.FromBase64String(c.PublicKey); }
        catch (FormatException) { return IdentityErrors.QuickLoginKeyInvalid; }
        if (key.Length > 512 || !crypto.IsValidPublicKey(key, c.KeyAlgorithm)) return IdentityErrors.QuickLoginKeyInvalid;

        var now = clock.GetUtcNow().UtcDateTime;
        var registration = QuickLoginRegistration.Register(userId, c.DeviceId.Trim(), c.Platform, key, c.KeyAlgorithm, now);
        // Only one enabled user per device: the previous registration on this device is disabled first, then the new one
        // is inserted, both in one transaction (the unique index on enabled device ids forbids an overlap).
        await uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await quickLogins.GetEnabledByDeviceAsync(registration.DeviceId, token);
            if (existing is not null)
            {
                existing.Disable(existing.UserId == userId ? QuickLoginDisabledReason.UserDisabled : QuickLoginDisabledReason.OtherUserEnabled, now);
                await uow.SaveChangesAsync(token);
            }
            quickLogins.Add(registration);
            await uow.SaveChangesAsync(token);
        }, ct);
        return new QuickLoginRegisteredDto(registration.Id, registration.DeviceId, registration.EnabledAt, Convert.ToHexString(registration.KeyFingerprint));
    }
}

public sealed record DisableQuickLoginCommand(string DeviceId) : ICommand<Unit>;

public sealed class DisableQuickLoginHandler(ICurrentUser current, IQuickLoginRepository quickLogins, IUnitOfWork uow, TimeProvider clock)
    : ICommandHandler<DisableQuickLoginCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DisableQuickLoginCommand c, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var registration = await quickLogins.GetEnabledByDeviceAsync(c.DeviceId, ct);
        if (registration is null || registration.UserId != userId) return IdentityErrors.QuickLoginNotRegistered;
        registration.Disable(QuickLoginDisabledReason.UserDisabled, clock.GetUtcNow().UtcDateTime);
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---- sign in: 1) ask for a one-time challenge  2) the device signs it after the OS verifies the user (Device PIN / biometrics)
public sealed record RequestQuickLoginChallengeCommand(string DeviceId) : ICommand<QuickLoginChallengeDto>;

public sealed class RequestQuickLoginChallengeHandler(
    IQuickLoginRepository quickLogins, IUserRepository users, ITokenService tokens, IUnitOfWork uow, TimeProvider clock, IdentitySecurityOptions options)
    : ICommandHandler<RequestQuickLoginChallengeCommand, QuickLoginChallengeDto>
{
    public async Task<Result<QuickLoginChallengeDto>> HandleAsync(RequestQuickLoginChallengeCommand c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.DeviceId) || c.DeviceId.Length > 100) return IdentityErrors.QuickLoginNotRegistered;
        var registration = await quickLogins.GetEnabledByDeviceAsync(c.DeviceId.Trim(), ct);
        var user = registration is null ? null : await users.GetByIdAsync(registration.UserId, UserIncludes.None, ct);
        if (registration is null || user is null || !user.CanSignIn) return IdentityErrors.QuickLoginNotRegistered;

        var nonce = tokens.GenerateNonce();
        var expires = clock.GetUtcNow().UtcDateTime.AddSeconds(options.QuickLoginChallengeSeconds);
        registration.IssueChallenge(nonce, expires);
        await uow.SaveChangesAsync(ct);
        return new QuickLoginChallengeDto(Convert.ToBase64String(nonce), expires);
    }
}

/// <summary>The device signs <c>challenge bytes + UTF-8(deviceId)</c> with its private key (ES256, DER signature).</summary>
public sealed record QuickLoginCommand(string DeviceId, string Challenge, string Signature) : ICommand<SessionDto>;

public sealed class QuickLoginValidator : AbstractValidator<QuickLoginCommand>
{
    public QuickLoginValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Challenge).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(1024);
    }
}

public sealed class QuickLoginHandler(
    IValidator<QuickLoginCommand> validator, IQuickLoginRepository quickLogins, IUserRepository users, IQuickLoginCrypto crypto,
    SessionService sessions, ILoginAuditRepository audit, IUnitOfWork uow, TimeProvider clock) : ICommandHandler<QuickLoginCommand, SessionDto>
{
    public async Task<Result<SessionDto>> HandleAsync(QuickLoginCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var now = clock.GetUtcNow().UtcDateTime;
        var deviceId = c.DeviceId.Trim();
        var registration = await quickLogins.GetEnabledByDeviceAsync(deviceId, ct);
        if (registration is null) return IdentityErrors.QuickLoginNotRegistered;

        byte[] nonce, signature;
        try { nonce = Convert.FromBase64String(c.Challenge); signature = Convert.FromBase64String(c.Signature); }
        catch (FormatException) { return IdentityErrors.QuickLoginProofInvalid; }

        // Single use: the challenge is consumed atomically before the signature is checked, so a replay always fails.
        if (!await quickLogins.TryConsumeChallengeAsync(registration.Id, nonce, now, ct)) return IdentityErrors.QuickLoginChallengeInvalid;

        var signed = nonce.Concat(Encoding.UTF8.GetBytes(deviceId)).ToArray();
        var user = await users.GetByIdAsync(registration.UserId, UserIncludes.None, ct);
        if (!crypto.VerifySignature(registration.PublicKey, registration.KeyAlgorithm, signed, signature))
        {
            audit.Add(LoginAudit.Record(registration.UserId, LoginMethod.QuickLogin, LoginOutcome.InvalidCredentials, deviceId, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.QuickLoginProofInvalid;
        }
        if (user is null || !user.CanSignIn)
        {
            audit.Add(LoginAudit.Record(registration.UserId, LoginMethod.QuickLogin, LoginOutcome.AccountInactive, deviceId, now));
            await uow.SaveChangesAsync(ct);
            return IdentityErrors.AccountInactive;
        }

        registration.MarkUsed(now);
        var session = sessions.Create(user.Id, ClientType.Mobile, deviceId);
        audit.Add(LoginAudit.Record(user.Id, LoginMethod.QuickLogin, LoginOutcome.Success, deviceId, now));
        await uow.SaveChangesAsync(ct);
        return new SessionDto(session.SessionId, session.UserId, session.Token, session.IssuedAt, session.ExpiresAt);
    }
}
