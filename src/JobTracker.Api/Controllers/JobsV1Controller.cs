using Asp.Versioning;
using JobTracker.Application.Jobs.Commands.CreateJob;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiVersion("1.0")]
public class JobsV1Controller : ApiControllerBase
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
}
