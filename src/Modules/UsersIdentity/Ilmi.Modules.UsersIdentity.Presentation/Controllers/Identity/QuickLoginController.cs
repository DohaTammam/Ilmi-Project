using Ilmi.Modules.UsersIdentity.Application.Commands.Identity;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ilmi.Modules.UsersIdentity.Presentation.Controllers;

/// <summary>
/// Quick Login (mobile). The Device PIN is verified by the device operating system and never reaches the backend:
/// the device registers a public key and later signs a one-time challenge.
/// </summary>
[ApiController]
[Route("api/auth/quick-login")]
[Produces("application/json")]
public sealed class QuickLoginController : ControllerBase
{
    [Authorize(AuthenticationSchemes = SessionAuthentication.Scheme)]
    [HttpPost("register")]
    [ProducesResponseType<QuickLoginRegisteredDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] RegisterQuickLoginCommand command,
        [FromServices] ICommandHandler<RegisterQuickLoginCommand, QuickLoginRegisteredDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToCreated(this);

    [Authorize(AuthenticationSchemes = SessionAuthentication.Scheme)]
    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] DisableQuickLoginCommand command,
        [FromServices] ICommandHandler<DisableQuickLoginCommand, Unit> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToNoContent(this);

    [HttpPost("challenge")]
    [ProducesResponseType<QuickLoginChallengeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Challenge([FromBody] RequestQuickLoginChallengeCommand command,
        [FromServices] ICommandHandler<RequestQuickLoginChallengeCommand, QuickLoginChallengeDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpPost("login")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] QuickLoginCommand command,
        [FromServices] ICommandHandler<QuickLoginCommand, SessionDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);
}
