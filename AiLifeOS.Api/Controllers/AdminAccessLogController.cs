using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/access-log")]
public sealed class AdminAccessLogController(AdminAccessLogger logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int take = 25, CancellationToken ct = default) =>
        Ok(await logger.RecentAsync(Math.Clamp(take, 1, 100), ct));
}