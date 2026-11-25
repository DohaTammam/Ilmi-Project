using Ilmi.Modules.UsersIdentity.Application.Commands.Identity;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ilmi.Modules.UsersIdentity.Presentation.Controllers;

/// <summary>Registration, email-only login with email OTP, Google/Apple sign-in, password flows and logout. No business logic lives here.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Step 1 of registration: validates the form and emails a one-time code.</summary>
    [HttpPost("register/request")]
    [ProducesResponseType<OtpRequestedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestRegistration([FromBody] RequestRegistrationCommand command,
        [FromServices] ICommandHandler<RequestRegistrationCommand, OtpRequestedDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    /// <summary>Step 2: verifies the code, creates the account and emails the generated password.</summary>
    [HttpPost("register/verify")]
    [ProducesResponseType<RegisteredDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> VerifyRegistration([FromBody] VerifyRegistrationCommand command,
        [FromServices] ICommandHandler<VerifyRegistrationCommand, RegisteredDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToCreated(this);

    /// <summary>Registration with Google or Apple: no password and no OTP.</summary>
    [HttpPost("register/external")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterWithExternalProvider([FromBody] RegisterWithExternalProviderCommand command,
        [FromServices] ICommandHandler<RegisterWithExternalProviderCommand, SessionDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToCreated(this);

    /// <summary>Step 1 of login: email and password. A one-time code is emailed.</summary>
    [HttpPost("login")]
    [ProducesResponseType<OtpRequestedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginWithPasswordCommand command,
        [FromServices] ICommandHandler<LoginWithPasswordCommand, OtpRequestedDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    /// <summary>Step 2 of login: the emailed code. Returns the session token (shown once).</summary>
    [HttpPost("login/verify-otp")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyLoginOtp([FromBody] VerifyLoginOtpCommand command,
        [FromServices] ICommandHandler<VerifyLoginOtpCommand, SessionDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpPost("login/external")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoginWithExternalProvider([FromBody] LoginWithExternalProviderCommand command,
        [FromServices] ICommandHandler<LoginWithExternalProviderCommand, SessionDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpPost("otp/resend")]
    [ProducesResponseType<OtpRequestedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpCommand command,
        [FromServices] ICommandHandler<ResendOtpCommand, OtpRequestedDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] RequestPasswordResetCommand command,
        [FromServices] ICommandHandler<RequestPasswordResetCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToNoContent(this);

    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command,
        [FromServices] ICommandHandler<ResetPasswordCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToNoContent(this);

    [Authorize(AuthenticationSchemes = SessionAuthentication.Scheme)]
    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command,
        [FromServices] ICommandHandler<ChangePasswordCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToNoContent(this);

    /// <summary>Explicit logout: revokes the current session.</summary>
    [Authorize(AuthenticationSchemes = SessionAuthentication.Scheme)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromServices] ICommandHandler<LogoutCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new LogoutCommand(), ct)).ToNoContent(this);
}
