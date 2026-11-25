using System.Security.Cryptography;
using System.Text;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Common;

public sealed record IssuedOtp(DateTime ExpiresAt, int ResendsRemaining);

/// <summary>What a successful verification yields. The payload is still encrypted; the caller decrypts it.</summary>
public sealed record VerifiedOtp(Guid? UserId, Guid? InitiatedByUserId, byte[]? EncryptedPayload);

/// <summary>
/// Email OTP lifecycle: issue, verify (single use, expiry, attempt limit with a temporary lock) and resend.
/// Only keyed hashes are stored; codes are never logged. Failure paths persist their counters themselves.
/// </summary>
public sealed class OtpService(
    IOtpRepository otps, IOtpCodeService codes, IIdentityEmailSender email, IUnitOfWork uow,
    TimeProvider clock, IdentitySecurityOptions options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IssuedOtp>> IssueAsync(OtpPurpose purpose, string emailAddress, Guid? userId, Guid? initiatedBy,
        byte[]? encryptedPayload, CancellationToken ct)
    {
        var normalized = EmailAddress.Normalize(emailAddress);
        var lockout = await otps.GetLockoutAsync(purpose, ScopeKey(purpose, normalized), ct);
        if (lockout?.IsLocked(Now) == true) return IdentityErrors.OtpLocked;

        var code = codes.GenerateCode();
        var challenge = OtpChallenge.Issue(purpose, emailAddress.Trim(), userId, initiatedBy,
            codes.Hash(purpose, normalized, code), Now, options.OtpLifetime, encryptedPayload);
        otps.Add(challenge);
        await uow.SaveChangesAsync(ct);
        await email.SendOtpAsync(challenge.TargetEmail, purpose, code, options.OtpLifetime, ct);
        return new IssuedOtp(challenge.ExpiresAt, options.OtpMaxResends - challenge.ResendCount);
    }

    public async Task<Result<VerifiedOtp>> VerifyAsync(OtpPurpose purpose, string emailAddress, string code, CancellationToken ct)
    {
        var now = Now;
        var normalized = EmailAddress.Normalize(emailAddress);
        var scope = ScopeKey(purpose, normalized);
        var lockout = await otps.GetLockoutAsync(purpose, scope, ct);
        if (lockout?.IsLocked(now) == true) return IdentityErrors.OtpLocked;

        var challenge = await otps.GetLatestAsync(purpose, emailAddress.Trim(), ct);
        if (challenge is null || !challenge.IsUsable(now)) return IdentityErrors.OtpInvalid;

        var supplied = codes.Hash(purpose, normalized, code.Trim());
        if (!CryptographicOperations.FixedTimeEquals(supplied, challenge.CodeHash))
        {
            challenge.RegisterFailedAttempt();
            if (lockout is null) { lockout = VerificationLockout.Create(purpose, scope); otps.Add(lockout); }
            var locked = lockout.RegisterFailure(now, options.OtpMaxAttempts, TimeSpan.FromMinutes(options.OtpLockMinutes));
            if (locked) challenge.Consume(now);   // a locked-out challenge can never verify
            await uow.SaveChangesAsync(ct);
            return locked ? IdentityErrors.OtpLocked : IdentityErrors.OtpInvalid;
        }

        var result = new VerifiedOtp(challenge.UserId, challenge.InitiatedByUserId, challenge.PendingPayload);
        challenge.Consume(now);
        lockout?.Reset();
        await uow.SaveChangesAsync(ct);
        return result;
    }

    public async Task<Result<IssuedOtp>> ResendAsync(OtpPurpose purpose, string emailAddress, CancellationToken ct)
    {
        var now = Now;
        var normalized = EmailAddress.Normalize(emailAddress);
        var lockout = await otps.GetLockoutAsync(purpose, ScopeKey(purpose, normalized), ct);
        if (lockout?.IsLocked(now) == true) return IdentityErrors.OtpLocked;

        var challenge = await otps.GetLatestAsync(purpose, emailAddress.Trim(), ct);
        if (challenge is null || challenge.IsConsumed) return IdentityErrors.OtpNothingToResend;
        if (!challenge.CanResend(options.OtpMaxResends)) return IdentityErrors.OtpResendLimit;

        var code = codes.GenerateCode();
        challenge.Reissue(codes.Hash(purpose, normalized, code), now, options.OtpLifetime);
        await uow.SaveChangesAsync(ct);
        await email.SendOtpAsync(challenge.TargetEmail, purpose, code, options.OtpLifetime, ct);
        return new IssuedOtp(challenge.ExpiresAt, options.OtpMaxResends - challenge.ResendCount);
    }

    /// <summary>Scope of a lockout: a hash of purpose and email, so no address is stored in the lockout table.</summary>
    internal static string ScopeKey(OtpPurpose purpose, string normalizedEmail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{normalizedEmail}")));
}
