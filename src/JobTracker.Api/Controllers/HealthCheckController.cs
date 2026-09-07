using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Api.Controllers;

[ApiController]
[Route("api/healthcheck")]
public class HealthCheckController : ControllerBase
{
    [HttpGet("/")]
    public IActionResult Get() => Ok();
}
