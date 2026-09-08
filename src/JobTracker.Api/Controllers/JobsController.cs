using Asp.Versioning;
using JobTracker.Application.Jobs.Commands.CompleteJob;
using JobTracker.Application.Jobs.Commands.CreateJob;
using JobTracker.Application.Jobs.Commands.StartJob;
using JobTracker.Application.Jobs.Queries.GetJobById;
using JobTracker.Application.Jobs.Queries.SearchJobs;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiVersion("1.0")]
public class JobsController : ApiControllerBase
{
    [HttpGet("{organizationId:guid}/{jobId:guid}")]
    public async Task<IActionResult> GetJobById([FromRoute] Guid organizationId, [FromRoute] Guid jobId)
    {
        var result = await Mediator.Send(new GetJobByIdQuery(organizationId, jobId));

        return result.IsFailure ? ToProblem(result.Error) : (IActionResult)Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> SearchJobs([FromQuery] SearchJobsQuery query)
    {
        var result = await Mediator.Send(query);

        if (result.IsFailure)
        {
            return ToProblem(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobCommand command)
    {
        var result = await Mediator.Send(command);

        if (result.IsFailure)
            return ToProblem(result.Error);

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
