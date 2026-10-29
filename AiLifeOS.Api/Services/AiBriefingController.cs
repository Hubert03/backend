using System.Security.Claims;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai/briefing")]
public sealed class AiBriefingController(DailyBriefingService briefing) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => RunAsync(false, ct);

    // POST so it goes through your existing CSRF protection
    [HttpPost("refresh")]
    public Task<IActionResult> Refresh(CancellationToken ct) => RunAsync(true, ct);

    private async Task<IActionResult> RunAsync(bool refresh, CancellationToken ct)
    {
        // ADAPT: use however your other controllers read the current user's id
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await briefing.GetAsync(userId, refresh, ct));
        }
        catch (BriefingRateLimitException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AiPlanConfigurationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AiPlanProviderException ex)
        {
            return Problem(detail: ex.FailureMessage ?? "The AI provider could not create a briefing.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}