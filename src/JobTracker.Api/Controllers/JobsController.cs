using Asp.Versioning;
using JobTracker.Application.Jobs.Commands.CompleteJob;
using JobTracker.Application.Jobs.Commands.CreateJob;
using JobTracker.Application.Jobs.Commands.StartJob;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiVersion("1.0")]
public class JobsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetJobs()
    {
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobCommand command)
    {
        var result = await Mediator.Send(command);

        if (result.IsFailure)
        {
            return ToProblem(result.Error);
        }

        return StatusCode(StatusCodes.Status201Created,
            new
            {
                id = result.Value
            });
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteJob([FromBody] CompleteJobCommand command)
    {
        var result = await Mediator.Send(command);

        return result.IsFailure ? ToProblem(result.Error) : NoContent();
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartJob([FromBody] StartJobCommand command)
    {
        var result = await Mediator.Send(command);

        return result.IsFailure ? ToProblem(result.Error) : NoContent();
    }
}
