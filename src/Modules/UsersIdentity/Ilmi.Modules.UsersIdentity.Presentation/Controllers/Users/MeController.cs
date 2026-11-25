using Ilmi.Modules.UsersIdentity.Application.Commands.Users;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Application.Queries.Users;
using Ilmi.SharedKernel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ilmi.Modules.UsersIdentity.Presentation.Controllers;

/// <summary>The signed-in user's own profile, settings, declared special needs and Rules.</summary>
[ApiController]
[Route("api/me")]
[Authorize(AuthenticationSchemes = SessionAuthentication.Scheme)]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile([FromServices] IQueryHandler<GetMyProfileQuery, UserProfileDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMyProfileQuery(), ct)).ToActionResult(this);

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateMyProfileCommand command,
        [FromServices] ICommandHandler<UpdateMyProfileCommand, UserProfileDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings([FromServices] IQueryHandler<GetMySettingsQuery, UserSettingsDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMySettingsQuery(), ct)).ToActionResult(this);

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateMySettingsCommand command,
        [FromServices] ICommandHandler<UpdateMySettingsCommand, UserSettingsDto> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    [HttpGet("accessibility-needs")]
    public async Task<IActionResult> GetAccessibilityNeeds(
        [FromServices] IQueryHandler<GetMyAccessibilityNeedsQuery, IReadOnlyList<DisabilityTypeDto>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMyAccessibilityNeedsQuery(), ct)).ToActionResult(this);

    [HttpPut("accessibility-needs")]
    public async Task<IActionResult> ReplaceAccessibilityNeeds([FromBody] ReplaceMyAccessibilityNeedsCommand command,
        [FromServices] ICommandHandler<ReplaceMyAccessibilityNeedsCommand, IReadOnlyList<DisabilityTypeDto>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToActionResult(this);

    /// <summary>The Rules assigned to the user. Read-only: Rule changes come from registration, age transitions and the Family module.</summary>
    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromServices] IQueryHandler<GetMyRulesQuery, IReadOnlyList<RuleDto>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMyRulesQuery(), ct)).ToActionResult(this);
}

/// <summary>Public lookups needed by the registration form.</summary>
[ApiController]
[Route("api/lookups")]
[Produces("application/json")]
public sealed class LookupsController : ControllerBase
{
    /// <summary>The four declared-need options (SRS DDL02).</summary>
    [HttpGet("disability-types")]
    public async Task<IActionResult> GetDisabilityTypes(
        [FromServices] IQueryHandler<GetDisabilityTypesQuery, IReadOnlyList<DisabilityTypeDto>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDisabilityTypesQuery(), ct)).ToActionResult(this);
}
