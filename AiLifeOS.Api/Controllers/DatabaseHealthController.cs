using AiLifeOS.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Route("api/health/database")]
public sealed class DatabaseHealthController(AiLifeOsDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable" });
        }

        return Ok(new { status = "connected" });
    }
}
