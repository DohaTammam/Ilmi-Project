using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Abstractions;

// ---------------------------------------------------------------------------------- persistence
public interface IUnitOfWork
{
    /// <exception cref="UniqueConstraintViolationException">A unique index rejected the write.</exception>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Runs the work, with its SaveChanges calls, in one database transaction.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct);
}

public sealed class UniqueConstraintViolationException(string constraintName) : Exception($"Unique constraint violated: {constraintName}")
{
    public string ConstraintName { get; } = constraintName;
}

[Flags]
public enum UserIncludes { None = 0, Settings = 1, AccessibilityNeeds = 2, Rules = 4, All = Settings | AccessibilityNeeds | Rules }

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, UserIncludes includes, CancellationToken ct);
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, UserIncludes includes, CancellationToken ct);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);
    /// <summary>True when a user other than a Child already uses this mobile number (Children may share numbers).</summary>
    Task<bool> MobileInUseAsync(string countryCode, string nationalNumber, CancellationToken ct);
    Task<bool> QrCodeExistsAsync(string qrCode, CancellationToken ct);
    Task<bool> AvatarExistsAsync(short avatarId, CancellationToken ct);
    Task<IReadOnlyList<DisabilityType>> GetDisabilityTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<Rule>> GetRulesAsync(CancellationToken ct);
}

/// <summary>
/// The Application's view of ASP.NET Core Identity. The implementation is in Infrastructure; the Application and the Domain never
/// see the Identity user manager or any other Identity type. Covers account creation with a password hash, external logins (AspNetUserLogins),
/// password check and change, and lockout. Social-only users simply have no password hash.
/// </summary>
public interface IUserAccountManager
{
    /// <summary>Creates the user (with its profile and settings). A null password creates a social-only account. Maps duplicate email, mobile or QR code and password-policy failures to errors.</summary>
    Task<Result> CreateAsync(User user, string? password, CancellationToken ct);
    Task<Result> AddExternalLoginAsync(User user, ExternalProvider provider, string subject, CancellationToken ct);
    Task<User?> FindByExternalLoginAsync(ExternalProvider provider, string subject, CancellationToken ct);
    Task<bool> HasPasswordAsync(User user, CancellationToken ct);
    Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct);
    Task<bool> IsLockedOutAsync(User user, CancellationToken ct);
    /// <summary>Counts a wrong password. Returns true when this failure locked the account.</summary>
    Task<bool> RegisterFailedPasswordAsync(User user, CancellationToken ct);
    Task ResetFailedPasswordsAsync(User user, CancellationToken ct);
    /// <summary>Replaces the password hash (policy-checked) and clears failures and the lock.</summary>
    Task<Result> SetPasswordAsync(User user, string newPassword, CancellationToken ct);
}

public interface IOtpRepository
{
    void Add(OtpChallenge challenge);
    Task<OtpChallenge?> GetLatestAsync(OtpPurpose purpose, string targetEmail, CancellationToken ct);
    void Add(VerificationLockout lockout);
    Task<VerificationLockout?> GetLockoutAsync(OtpPurpose purpose, string scopeKey, CancellationToken ct);
}

public interface ISessionRepository
{
    void Add(UserSession session);
    Task<UserSession?> GetByIdAsync(Guid sessionId, CancellationToken ct);
    Task<UserSession?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct);
    Task<IReadOnlyList<UserSession>> GetActiveForUserAsync(Guid userId, CancellationToken ct);
}

public interface IQuickLoginRepository
{
    void Add(QuickLoginRegistration registration);
    Task<QuickLoginRegistration?> GetEnabledByDeviceAsync(string deviceId, CancellationToken ct);
    Task<IReadOnlyList<QuickLoginRegistration>> GetEnabledForUserAsync(Guid userId, CancellationToken ct);
    /// <summary>
    /// Atomically consumes the outstanding challenge (single use). Returns false when the nonce does not match,
    /// was already used or has expired.
    /// </summary>
    Task<bool> TryConsumeChallengeAsync(Guid registrationId, byte[] nonce, DateTime now, CancellationToken ct);
}

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);
}

// ---------------------------------------------------------------------------------- security and integrations
public interface IPasswordGenerator
{
    /// <summary>A random password that satisfies the password policy (GSR014).</summary>
    string Generate();
}

public interface IOtpCodeService
{
    string GenerateCode();
    /// <summary>Keyed hash (HMAC) bound to purpose and email. The code itself is never stored or logged.</summary>
    byte[] Hash(OtpPurpose purpose, string normalizedEmail, string code);
}

public interface ITokenService
{
    /// <summary>A new opaque session token (shown to the client once).</summary>
    string GenerateToken();
    byte[] HashToken(string token);
    byte[] GenerateNonce();
}

public interface IPayloadProtector
{
    /// <summary>Authenticated encryption. The result is ciphertext bytes, stored as varbinary(max).</summary>
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>Cryptographic proof for Quick Login. The Device PIN is verified by the device OS and never reaches this port.</summary>
public interface IQuickLoginCrypto
{
    bool IsValidPublicKey(byte[] subjectPublicKeyInfo, string algorithm);
    bool VerifySignature(byte[] subjectPublicKeyInfo, string algorithm, byte[] data, byte[] signature);
}

public sealed record ExternalIdentity(string Subject, string Email, bool EmailVerified);

/// <summary>Verifies a Google or Apple ID token. Requires provider configuration: see the integration notes.</summary>
public interface IExternalIdentityVerifier
{
    Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string idToken, CancellationToken ct);
}

/// <summary>Sends transactional email. The Notifications module will own delivery (outbox) and replace this adapter.</summary>
public interface IIdentityEmailSender
{
    Task SendOtpAsync(string email, OtpPurpose purpose, string code, TimeSpan lifetime, CancellationToken ct);
    /// <summary>The generated password is sent by email (SRS). It is never stored in plain text and never logged.</summary>
    Task SendGeneratedPasswordAsync(string email, string password, CancellationToken ct);
}

/// <summary>Thrown by adapters whose external integration is not configured yet. Surfaced as HTTP 503.</summary>
public sealed class ExternalIntegrationException(string integration, string message) : Exception(message)
{
    public string Integration { get; } = integration;
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
}
