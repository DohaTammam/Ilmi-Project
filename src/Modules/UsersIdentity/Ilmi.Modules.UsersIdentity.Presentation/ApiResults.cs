using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.SharedKernel.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ilmi.Modules.UsersIdentity.Presentation;

/// <summary>Maps application results to HTTP. Failures become RFC 7807 problem details carrying a stable <c>code</c>.</summary>
public static class ApiResults
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase c) =>
        result.IsSuccess ? c.Ok(result.Value) : Problem(result.Error!, c);

    public static IActionResult ToNoContent(this Result result, ControllerBase c) =>
        result.IsSuccess ? c.NoContent() : Problem(result.Error!, c);

    public static IActionResult ToCreated<T>(this Result<T> result, ControllerBase c) =>
        result.IsSuccess ? c.StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error!, c);

    private static IActionResult Problem(Error error, ControllerBase c)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Locked => StatusCodes.Status423Locked,
            _ => StatusCodes.Status400BadRequest
        };
        var problem = new ProblemDetails { Status = status, Title = error.Code, Detail = error.Message };
        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}

/// <summary>An integration that is not configured yet (email delivery, Google, Apple) answers 503, never a silent success.</summary>
public sealed class IntegrationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not ExternalIntegrationException integration) return false;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "integration.not_configured",
            Detail = integration.Message
        };
        problem.Extensions["code"] = "integration.not_configured";
        problem.Extensions["integration"] = integration.Integration;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
