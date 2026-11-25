using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>
/// One verification attempt by email OTP. Only a keyed hash of the code is stored, never the code. A resend re-issues the
/// code on the SAME row so the attempt and resend counters cannot be reset by asking for a new code.
/// </summary>
public sealed class OtpChallenge
{
    private OtpChallenge() { }

    public Guid Id { get; private set; }
    public OtpPurpose Purpose { get; private set; }
    public OtpChannel Channel { get; private set; } = OtpChannel.Email;
    public string TargetEmail { get; private set; } = "";
    public Guid? UserId { get; private set; }
    /// <summary>The Parent, for dependent verification.</summary>
    public Guid? InitiatedByUserId { get; private set; }
    public byte[] CodeHash { get; private set; } = [];
    public DateTime ExpiresAt { get; private set; }
    public byte AttemptCount { get; private set; }
    public byte ResendCount { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    /// <summary>Encrypted bytes (ciphertext), never Base64 text. Cleared when the challenge is consumed.</summary>
    public byte[]? PendingPayload { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static OtpChallenge Issue(OtpPurpose purpose, string targetEmail, Guid? userId, Guid? initiatedByUserId,
        byte[] codeHash, DateTime now, TimeSpan lifetime, byte[]? encryptedPayload = null) => new()
    {
        Id = SequentialGuid.Create(), Purpose = purpose, Channel = OtpChannel.Email, TargetEmail = targetEmail,
        UserId = userId, InitiatedByUserId = initiatedByUserId, CodeHash = codeHash,
        ExpiresAt = now + lifetime, PendingPayload = encryptedPayload, CreatedAt = now
    };

    public bool IsConsumed => ConsumedAt is not null;
    public bool IsExpired(DateTime now) => now >= ExpiresAt;
    public bool IsUsable(DateTime now) => !IsConsumed && !IsExpired(now);
    public bool CanResend(int maxResends) => ResendCount < maxResends;

    public void RegisterFailedAttempt() { if (AttemptCount < byte.MaxValue) AttemptCount++; }

    /// <summary>Single use: after this the challenge can never verify again and the stored payload is wiped.</summary>
    public void Consume(DateTime now)
    {
        ConsumedAt = now;
        PendingPayload = null;
    }

    /// <summary>Issues a new code on the same challenge. The caller enforces the resend limit with <see cref="CanResend"/>.</summary>
    public void Reissue(byte[] newCodeHash, DateTime now, TimeSpan lifetime)
    {
        CodeHash = newCodeHash;
        ExpiresAt = now + lifetime;
        ResendCount++;
    }
}

/// <summary>
/// Temporary lock after too many failed verifications. Primary key (Purpose, ScopeKey): ScopeKey is a hash of the target
/// email, or the Parent's user id for the Add Dependent lockout. It outlives the challenge so a new OTP cannot bypass it.
/// </summary>
public sealed class VerificationLockout
{
    private VerificationLockout() { }

    public OtpPurpose Purpose { get; private set; }
    public string ScopeKey { get; private set; } = "";
    public byte FailedCount { get; private set; }
    public DateTime? LastFailedAt { get; private set; }
    public DateTime? LockedUntil { get; private set; }

    public static VerificationLockout Create(OtpPurpose purpose, string scopeKey) => new() { Purpose = purpose, ScopeKey = scopeKey };

    public bool IsLocked(DateTime now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Counts a failure. When <paramref name="threshold"/> is null (not decided by the business) failures are counted
    /// but no automatic lock happens. Returns true when this failure locked the scope.
    /// </summary>
    public bool RegisterFailure(DateTime now, int? threshold, TimeSpan lockDuration)
    {
        if (LockedUntil is { } until && until <= now) { LockedUntil = null; FailedCount = 0; }
        if (FailedCount < byte.MaxValue) FailedCount++;
        LastFailedAt = now;
        if (threshold is null || FailedCount < threshold) return false;
        LockedUntil = now + lockDuration;
        FailedCount = 0;
        return true;
    }

    public void Lock(DateTime now, TimeSpan duration) { LockedUntil = now + duration; FailedCount = 0; }

    public void Reset() { FailedCount = 0; LastFailedAt = null; LockedUntil = null; }
}
