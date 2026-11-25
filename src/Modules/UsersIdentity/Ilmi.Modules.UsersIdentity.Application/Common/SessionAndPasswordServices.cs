using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Common;

public sealed record IssuedSession(Guid SessionId, Guid UserId, string Token, DateTime IssuedAt, DateTime? ExpiresAt);

/// <summary>
/// Creates and revokes sessions. The token is opaque and shown once; only its SHA-256 is stored.
/// A session stays valid until it is revoked (logout or system revocation): there is no inactivity timeout.
/// </summary>
public sealed class SessionService(ISessionRepository sessions, ITokenService tokens, TimeProvider clock)
{
    public IssuedSession Create(Guid userId, ClientType clientType, string? deviceId)
    {
        var token = tokens.GenerateToken();
        var session = UserSession.Create(userId, clientType, deviceId, tokens.HashToken(token), clock.GetUtcNow().UtcDateTime);
        sessions.Add(session);
        return new IssuedSession(session.Id, userId, token, session.IssuedAt, session.ExpiresAt);
    }

    public async Task<Result> RevokeAsync(Guid sessionId, SessionRevokedReason reason, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct);
        if (session is null) return IdentityErrors.SessionNotFound;
        session.Revoke(reason, clock.GetUtcNow().UtcDateTime);
        return Result.Success();
    }

    public async Task RevokeAllForUserAsync(Guid userId, SessionRevokedReason reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var s in await sessions.GetActiveForUserAsync(userId, ct)) s.Revoke(reason, now);
    }
}

/// <summary>Password policy (GSR014: 12 characters minimum; no other composition rule is specified).</summary>
public sealed class PasswordPolicy(IdentitySecurityOptions options)
{
    public bool IsSatisfiedBy(string? password) => !string.IsNullOrEmpty(password) && password.Length >= options.MinPasswordLength;
}

/// <summary>Disables the Quick Login registrations of a user (for example after a password change).</summary>
public sealed class QuickLoginDisabler(IQuickLoginRepository quickLogins, TimeProvider clock)
{
    public async Task DisableAllForUserAsync(Guid userId, QuickLoginDisabledReason reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var r in await quickLogins.GetEnabledForUserAsync(userId, ct)) r.Disable(reason, now);
    }

    /// <summary>When a different user signs in on a device, the Quick Login enabled there for another user is turned off.</summary>
    public async Task DisableOtherUsersOnDeviceAsync(string deviceId, Guid signedInUserId, CancellationToken ct)
    {
        var registration = await quickLogins.GetEnabledByDeviceAsync(deviceId, ct);
        if (registration is not null && registration.UserId != signedInUserId)
            registration.Disable(QuickLoginDisabledReason.OtherUserLoggedIn, clock.GetUtcNow().UtcDateTime);
    }
}
