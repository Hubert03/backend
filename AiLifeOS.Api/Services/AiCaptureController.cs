using System.Globalization;
using System.Security.Claims;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai/capture")]
public sealed class AiCaptureController(SmartCaptureService capture) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CaptureRequest request, CancellationToken ct)
    {
        // ADAPT: use the same user-id lookup as your other controllers
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 300)
            return Problem(detail: "Enter a sentence of up to 300 characters.", statusCode: 400);

        if (!DateOnly.TryParseExact(request.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var today)
            || Math.Abs(today.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber) > 1)
            return Problem(detail: "The date from your browser looks wrong.", statusCode: 400);

        try
        {
            return Ok(await capture.CaptureAsync(userId, text, today, ct));
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
            return Problem(detail: ex.FailureMessage ?? "The AI provider could not create a draft.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}