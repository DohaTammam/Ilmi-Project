using System.Security.Cryptography;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>
/// A revocable server-side session. Only a hash of the token is stored. A Web session stays active until it is
/// explicitly revoked (logout): there is NO inactivity timeout. <see cref="ExpiresAt"/> is null unless a limit is ever defined.
/// </summary>
public sealed class UserSession
{
    private UserSession() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ClientType ClientType { get; private set; }
    public string? DeviceId { get; private set; }
    public byte[] RefreshTokenHash { get; private set; } = [];
    public DateTime IssuedAt { get; private set; }
    /// <summary>Informational only. Never used to expire a session.</summary>
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public SessionRevokedReason? RevokedReason { get; private set; }

    public static UserSession Create(Guid userId, ClientType clientType, string? deviceId, byte[] tokenHash, DateTime now) =>
        new() { Id = SequentialGuid.Create(), UserId = userId, ClientType = clientType, DeviceId = deviceId, RefreshTokenHash = tokenHash, IssuedAt = now };

    public bool IsActive(DateTime now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Idempotent: revoking a revoked session keeps the original reason and time.</summary>
    public void Revoke(SessionRevokedReason reason, DateTime now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
    }

    public void Touch(DateTime now) => LastUsedAt = now;
}

public static class KeyAlgorithms
{
    /// <summary>ECDSA over P-256 with SHA-256; the public key is a DER SubjectPublicKeyInfo.</summary>
    public const string Es256 = "ES256";
}

/// <summary>
/// Device-bound Quick Login. The Device PIN belongs to the operating system: this module never receives or stores it,
/// stores no app-level passcode and no biometric data. It stores only the device's PUBLIC key; sign-in proves possession
/// of the private key by signing a one-time server challenge. Each enable creates a new row, so history is kept.
/// </summary>
public sealed class QuickLoginRegistration
{
    private QuickLoginRegistration() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string DeviceId { get; private set; } = "";
    public DevicePlatform Platform { get; private set; }
    public byte[] PublicKey { get; private set; } = [];
    public string KeyAlgorithm { get; private set; } = KeyAlgorithms.Es256;
    /// <summary>SHA-256 of <see cref="PublicKey"/>.</summary>
    public byte[] KeyFingerprint { get; private set; } = [];
    public byte[]? ChallengeNonce { get; private set; }
    public DateTime? ChallengeExpiresAt { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime EnabledAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? DisabledAt { get; private set; }
    public QuickLoginDisabledReason? DisabledReason { get; private set; }

    public static QuickLoginRegistration Register(Guid userId, string deviceId, DevicePlatform platform, byte[] publicKey, string keyAlgorithm, DateTime now) => new()
    {
        Id = SequentialGuid.Create(), UserId = userId, DeviceId = deviceId, Platform = platform, PublicKey = publicKey,
        KeyAlgorithm = keyAlgorithm, KeyFingerprint = SHA256.HashData(publicKey), IsEnabled = true, EnabledAt = now
    };

    public void Disable(QuickLoginDisabledReason reason, DateTime now)
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        DisabledAt = now;
        DisabledReason = reason;
        ChallengeNonce = null;
        ChallengeExpiresAt = null;
    }

    /// <summary>Replaces any outstanding challenge. One challenge per registration; it is single use.</summary>
    public void IssueChallenge(byte[] nonce, DateTime expiresAt)
    {
        ChallengeNonce = nonce;
        ChallengeExpiresAt = expiresAt;
    }

    public void MarkUsed(DateTime now) => LastUsedAt = now;
}

/// <summary>Append-only login log. No IP address or user agent is stored (logging policy is deferred).</summary>
public sealed class LoginAudit : IAppendOnly
{
    private LoginAudit() { }

    public long Id { get; private set; }
    public Guid? UserId { get; private set; }
    public LoginMethod LoginMethod { get; private set; }
    public LoginOutcome Outcome { get; private set; }
    public string? DeviceId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    public static LoginAudit Record(Guid? userId, LoginMethod method, LoginOutcome outcome, string? deviceId, DateTime now) =>
        new() { UserId = userId, LoginMethod = method, Outcome = outcome, DeviceId = deviceId, OccurredAt = now };
}
