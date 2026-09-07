using Asp.Versioning;
using JobTracker.Application.Jobs.Queries.SearchJobs;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiVersion("1.0")]
public class JobsV1Controller : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetJobs()
    {
        var query = new SearchJobsQuery();
        return Ok("message test");
    }
}

[ApiVersion("2.0")]
public class JobsV2Controller : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetJobs()
    {
        var query = new SearchJobsQuery();
        return Ok(await Mediator.Send(query));
    }
}
