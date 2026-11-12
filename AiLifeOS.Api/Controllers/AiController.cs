using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using System.Net;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public sealed class AiController(
    OpenRouterPlanService planService,
    KnowledgeAssistantService knowledgeAssistant,
    DailyCopilotService copilot,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<ActionResult<PlanResponse>> CreatePlan(
        CreatePlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Objective)
            || !IsValidTarget(request.Target)
            || (request.Context is not null && string.IsNullOrWhiteSpace(request.Context))
            || request.Constraints is { Count: > 20 }
            || request.Constraints?.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 500) == true)
        {
            return BadRequest(new
            {
                message = "Objective and target must be valid; optional context must be nonblank plain text; provide no more than 20 nonblank constraints of at most 500 characters."
            });
        }

        var promptLength = request.Objective.Length
            + (request.Context?.Length ?? 0)
            + (request.Constraints?.Sum(item => item.Length) ?? 0);
        if (promptLength > 6000)
        {
            return BadRequest(new { message = "Objective, context, and constraints must total no more than 6,000 characters." });
        }

        if (ContainsUnsupportedControlCharacters(request.Objective)
            || (request.Context is not null && ContainsUnsupportedControlCharacters(request.Context))
            || request.Constraints?.Any(ContainsUnsupportedControlCharacters) == true)
        {
            return BadRequest(new { message = "Context must contain plain text only." });
        }

        try
        {
            var plan = await planService.CreatePlanAsync(
                new PlanRequest(
                    request.Objective.Trim(),
                    string.IsNullOrWhiteSpace(request.Context) ? null : request.Context.Trim(),
                    request.TargetDate,
                    request.Constraints?.Select(item => item.Trim()).ToArray(),
                    request.Target),
                cancellationToken);
            return Ok(plan);
        }
        catch (AiPlanConfigurationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (AiPlanProviderException exception)
        {
            var message = exception.FailureMessage ?? exception.ProviderStatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "OpenRouter rejected the stored API key (HTTP 401). Update the key in Settings and use an active OpenRouter key.",
                HttpStatusCode.PaymentRequired =>
                    "OpenRouter reports that this request requires credits or billing (HTTP 402). Check the account and try a free model if available.",
                HttpStatusCode.Forbidden =>
                    "OpenRouter denied access to this key or model (HTTP 403). Check the key permissions and model availability.",
                HttpStatusCode.NotFound =>
                    "OpenRouter could not find the configured model or endpoint (HTTP 404). Update Settings with a valid model and the default OpenRouter URL.",
                HttpStatusCode.BadRequest =>
                    "OpenRouter rejected the request (HTTP 400). The selected model may not support JSON responses; choose a model that supports JSON output and update Settings.",
                HttpStatusCode.TooManyRequests =>
                    "OpenRouter is rate-limiting this model (HTTP 429). Wait and retry, or select another available model.",
                { } statusCode when (int)statusCode >= 500 =>
                    $"OpenRouter is temporarily unavailable (HTTP {(int)statusCode}). Please retry later.",
                not null =>
                    $"OpenRouter rejected the request (HTTP {(int)exception.ProviderStatusCode.Value}). Check the selected model and key in Settings.",
                _ =>
                    "OpenRouter responded, but the response was not valid plan JSON. Try again or configure a model that supports JSON output in Settings."
            };
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The AI planning provider could not be reached."
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                message = "The AI planning provider timed out."
            });
        }
    }

    [HttpPost("ask-notes")]
    public async Task<ActionResult<KnowledgeAnswerResponse>> AskNotes(
        AskNotesRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });

        if (string.IsNullOrWhiteSpace(request.Question)
            || request.Question.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            return BadRequest(new { message = "Enter a plain-text question." });

        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await knowledgeAssistant.AskAsync(
                userId,
                request.Question.Trim(),
                cancellationToken));
        }
        catch (AiPlanConfigurationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (AiPlanProviderException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = exception.FailureMessage ?? "The AI provider could not answer from your notes."
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The AI planning provider could not be reached."
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                message = "The AI provider timed out. Try a shorter question."
            });
        }
    }

    [HttpPost("copilot")]
    public async Task<ActionResult<DailyCopilotResponse>> CreateCopilotPlan(
        DailyCopilotRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });

        if (request.Focus?.Length > 1000
            || request.Focus?.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t') == true)
            return BadRequest(new { message = "Focus must be plain text and no more than 1,000 characters." });

        try
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            return Ok(await copilot.CreateAsync(
                userId,
                request.Focus,
                cancellationToken));
        }
        catch (BriefingRateLimitException exception)
        {
            Response.Headers.RetryAfter = exception.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = exception.Message });
        }
        catch (AiPlanConfigurationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (AiPlanProviderException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = exception.FailureMessage ?? "The AI provider could not create a daily plan."
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The AI planning provider could not be reached."
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                message = "The AI provider timed out. Please try again."
            });
        }
    }

    private async Task<bool> HasValidCsrfTokenAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool ContainsUnsupportedControlCharacters(string value) =>
        value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');

    private static bool IsValidTarget(string target) =>
        target is "overview" or "tasks" or "goals" or "notes";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePlanRequest(
    [Required, StringLength(4000, MinimumLength = 3)] string Objective,
    [StringLength(10000)] string? Context,
    DateOnly? TargetDate,
    [MaxLength(20)] IReadOnlyList<string>? Constraints,
    [Required, RegularExpression("^(overview|tasks|goals|notes)$")] string Target);

public sealed record AskNotesRequest(
    [Required, StringLength(1500, MinimumLength = 3)] string Question);

public sealed record DailyCopilotRequest(
    [StringLength(1000)] string? Focus);
