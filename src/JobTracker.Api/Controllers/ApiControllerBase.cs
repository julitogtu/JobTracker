using JobTracker.Api.Middleware;
using JobTracker.Application.Common.Results;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender mediator = null!;

    protected ISender Mediator => mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected ObjectResult ToProblem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = Problem(
            statusCode: statusCode,
            title: error.Code,
            detail: error.Description);

        if (problem.Value is ProblemDetails details)
        {
            details.Extensions["correlationId"] = HttpContext.GetCorrelationId();
        }

        return problem;
    }
}
