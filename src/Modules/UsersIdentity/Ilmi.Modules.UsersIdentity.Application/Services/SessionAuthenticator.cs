using Ilmi.Modules.UsersIdentity.Application.Abstractions;

namespace Ilmi.Modules.UsersIdentity.Application.Services;

public sealed record AuthenticatedSession(Guid SessionId, Guid UserId, string UserKind);

/// <summary>Resolves an opaque session token to a signed-in user. Used by the API authentication handler.</summary>
public interface ISessionAuthenticator
{
    /// <summary>Null when the token is unknown, the session is revoked, or the user can no longer sign in.</summary>
    Task<AuthenticatedSession?> AuthenticateAsync(string token, CancellationToken ct);
}

public sealed class SessionAuthenticator(ISessionRepository sessions, IUserRepository users, ITokenService tokens, TimeProvider clock) : ISessionAuthenticator
{
    public async Task<AuthenticatedSession?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var session = await sessions.GetByTokenHashAsync(tokens.HashToken(token), ct);
        if (session is null || !session.IsActive(clock.GetUtcNow().UtcDateTime)) return null;
        var user = await users.GetByIdAsync(session.UserId, UserIncludes.None, ct);
        // Deactivation or closing the administrative gate takes effect immediately, even for sessions that are still open.
        return user is { CanSignIn: true } ? new AuthenticatedSession(session.Id, user.Id, user.UserKind.ToString()) : null;
    }
}
