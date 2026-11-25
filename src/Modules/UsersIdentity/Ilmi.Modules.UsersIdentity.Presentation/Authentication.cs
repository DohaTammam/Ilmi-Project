using System.Security.Claims;
using System.Text.Encodings.Web;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Ilmi.Modules.UsersIdentity.Presentation;

public static class SessionAuthentication
{
    public const string Scheme = "IlmiSession";
    public const string SessionIdClaim = "ilmi:sid";
    public const string UserKindClaim = "ilmi:user_kind";
}

/// <summary>
/// Authenticates "Authorization: Bearer &lt;session token&gt;". The token is opaque; the session is looked up by the hash of the token,
/// so logout and revocation take effect immediately and a Web session never times out on inactivity.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, Microsoft.Extensions.Logging.ILoggerFactory logger, UrlEncoder encoder, ISessionAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var session = await authenticator.AuthenticateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("The session is not valid.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new Claim(SessionAuthentication.SessionIdClaim, session.SessionId.ToString()),
            new Claim(SessionAuthentication.UserKindClaim, session.UserKind)
        ], SessionAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SessionAuthentication.Scheme));
    }
}

/// <summary>The signed-in user of the current HTTP request, for the Application layer.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);
    public Guid? SessionId => Read(SessionAuthentication.SessionIdClaim);

    private Guid? Read(string type) =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(type), out var id) ? id : null;
}
