using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
//public sealed class SettingsController(AiLifeOsDbContext dbContext,IAntiforgery antiforgery,ILogger<SettingsController> logger) : ControllerBase
public sealed class SettingsController(AiLifeOsDbContext dbContext,IAntiforgery antiforgery,ILogger<SettingsController> logger,OpenRouterApiKeyProtector apiKeyProtector) : ControllerBase
{
    [HttpGet("ai-provider")]
    public async Task<ActionResult<AiProviderSettingsResponse>> GetAiProviderSettings(
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.AiProviderSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SettingsId == 1, cancellationToken);

        return Ok(ToResponse(settings));
    }


    [HttpPut("ai-provider")]
    public async Task<ActionResult<AiProviderSettingsResponse>> UpdateAiProviderSettings(
        UpdateAiProviderSettingsRequest request,
        CancellationToken cancellationToken)
    {
        string stage = "Request started";

        try
        {
            logger.LogInformation(
                "AI provider settings update started. TraceId: {TraceId}",
                HttpContext.TraceIdentifier);

            stage = "CSRF validation";

            if (!await HasValidCsrfTokenAsync())
            {
                logger.LogWarning(
                    "AI provider settings update rejected: CSRF validation failed.");

                return BadRequest(new
                {
                    message = "Invalid or missing anti-forgery token."
                });
            }

            stage = "Input validation";

            var apiUrl = request.ApiUrl.Trim();
            var model = request.Model.Trim();
            var apiKey = request.ApiKey?.Trim();

            if (!OpenRouterSettingsPolicy.IsAllowedApiUrl(apiUrl))
            {
                return BadRequest(new
                {
                    message = "The API URL must be the HTTPS OpenRouter chat completions endpoint."
                });
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return BadRequest(new
                {
                    message = "The model name cannot be empty."
                });
            }

            if (apiKey?.Any(char.IsControl) == true)
            {
                return BadRequest(new
                {
                    message = "The API key must not contain control characters."
                });
            }

            if (request.ClearApiKey && !string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(new
                {
                    message = "Provide a new API key or clear the saved key, not both."
                });
            }

            if (!OperatingSystem.IsWindows())
            {
                logger.LogError("AI provider settings update failed: Windows DPAPI is unavailable.");

                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Saving OpenRouter keys is only supported on Windows."
                });
            }

            stage = "Existing settings database lookup";

            var settings = await dbContext.AiProviderSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingsId == 1,cancellationToken);

            byte[] encryptedApiKey;

            if (request.ClearApiKey)
            {
                stage = "Clearing API key";
                encryptedApiKey = [];
            }
            else if (!string.IsNullOrWhiteSpace(apiKey))
            {
                stage = "DPAPI API key encryption";
                //logger.LogInformation("Starting DPAPI encryption. Windows: {IsWindows}",OperatingSystem.IsWindows());
                encryptedApiKey = apiKeyProtector.ProtectApiKey(apiKey);
                logger.LogInformation("DPAPI encryption completed successfully.");
            }
            else if (settings is not null && settings.EncryptedApiKey.Length > 0)
            {
                stage = "Reusing existing encrypted API key";
                encryptedApiKey = settings.EncryptedApiKey;
            }
            else
            {
                return BadRequest(new
                {
                    message = "Enter an API key before saving the provider settings."
                });
            }

            stage = "SQL stored procedure execution";

            logger.LogInformation("Executing AiProviderSettings_Upsert. TraceId: {TraceId}",HttpContext.TraceIdentifier);

            var updatedAtUtc = DateTime.UtcNow;
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC dbo.AiProviderSettings_Upsert @ApiUrl={apiUrl}, @Model={model}, @EncryptedApiKey={encryptedApiKey}, @UpdatedAtUtc={updatedAtUtc}",
                cancellationToken);

            logger.LogInformation("AiProviderSettings_Upsert executed successfully.");

            stage = "Reading saved settings";

            var savedSettings = await dbContext.AiProviderSettings.AsNoTracking().SingleAsync(item => item.SettingsId == 1,cancellationToken);
            logger.LogInformation("AI provider settings saved and read back successfully. TraceId: {TraceId}",HttpContext.TraceIdentifier);

            return Ok(ToResponse(savedSettings));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "AI provider settings update was cancelled. Stage: {Stage}",
                stage);

            throw;
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;

            logger.LogError(
                ex,
                "AI PROVIDER SETTINGS UPDATE FAILED. Stage: {Stage}, TraceId: {TraceId}",
                stage,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Failed to save AI provider settings.",
                stage = stage,
                errorType = ex.GetType().FullName,
                traceId = traceId
            });
        }
    }


    //[HttpPut("ai-provider")]
    //public async Task<ActionResult<AiProviderSettingsResponse>> UpdateAiProviderSettings(
    //    UpdateAiProviderSettingsRequest request,
    //    CancellationToken cancellationToken)
    //{
    //    if (!await HasValidCsrfTokenAsync())
    //    {
    //        return BadRequest(new { message = "Invalid or missing anti-forgery token." });
    //    }

    //    var apiUrl = request.ApiUrl.Trim();
    //    var model = request.Model.Trim();
    //    var apiKey = request.ApiKey?.Trim();
    //    if (!OpenRouterSettingsPolicy.IsAllowedApiUrl(apiUrl))
    //    {
    //        return BadRequest(new
    //        {
    //            message = "The API URL must be the HTTPS OpenRouter chat completions endpoint."
    //        });
    //    }

    //    if (string.IsNullOrWhiteSpace(model))
    //    {
    //        return BadRequest(new { message = "The model name cannot be empty." });
    //    }

    //    if (apiKey?.Any(char.IsControl) == true)
    //    {
    //        return BadRequest(new { message = "The API key must not contain control characters." });
    //    }

    //    if (request.ClearApiKey && !string.IsNullOrWhiteSpace(apiKey))
    //    {
    //        return BadRequest(new { message = "Provide a new API key or clear the saved key, not both." });
    //    }

    //    if (!OperatingSystem.IsWindows())
    //    {
    //        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
    //        {
    //            message = "Saving OpenRouter keys is only supported on Windows because the key is protected with Windows DPAPI."
    //        });
    //    }

    //    var settings = await dbContext.AiProviderSettings.AsNoTracking()
    //        .SingleOrDefaultAsync(item => item.SettingsId == 1, cancellationToken);
    //    byte[] encryptedApiKey;
    //    if (request.ClearApiKey)
    //    {
    //        encryptedApiKey = [];
    //    }
    //    else if (!string.IsNullOrWhiteSpace(apiKey))
    //    {
    //        //encryptedApiKey = OpenRouterSettingsPolicy.ProtectApiKey(apiKey);
    //        try
    //        {
    //            encryptedApiKey = OpenRouterSettingsPolicy.ProtectApiKey(apiKey);
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.Error.WriteLine(
    //                "AI PROVIDER KEY ENCRYPTION FAILED: " + ex);
    //            throw;
    //        }
    //    }
    //    else if (settings is not null && settings.EncryptedApiKey.Length > 0)
    //    {
    //        encryptedApiKey = settings.EncryptedApiKey;
    //    }
    //    else
    //    {
    //        return BadRequest(new { message = "Enter an API key before saving the provider settings." });
    //    }

    //    var updatedAtUtc = DateTime.UtcNow;
    //    await dbContext.Database.ExecuteSqlInterpolatedAsync(
    //        $"EXEC dbo.AiProviderSettings_Upsert @ApiUrl={apiUrl}, @Model={model}, @EncryptedApiKey={encryptedApiKey}, @UpdatedAtUtc={updatedAtUtc}",
    //        cancellationToken);
    //    var savedSettings = await dbContext.AiProviderSettings.AsNoTracking()
    //        .SingleAsync(item => item.SettingsId == 1, cancellationToken);
    //    return Ok(ToResponse(savedSettings));
    //}

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

    private static AiProviderSettingsResponse ToResponse(AiProviderSettings? settings) =>
        new(
            settings?.ApiUrl ?? OpenRouterSettingsPolicy.DefaultApiUrl,
            settings?.Model ?? OpenRouterSettingsPolicy.DefaultModel,
            settings?.EncryptedApiKey is { Length: > 0 },
            settings?.UpdatedAtUtc);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAiProviderSettingsRequest(
    [Required, StringLength(500)] string ApiUrl,
    [Required, StringLength(100)] string Model,
    [StringLength(500)] string? ApiKey,
    bool ClearApiKey);

public sealed record AiProviderSettingsResponse(
    string ApiUrl,
    string Model,
    bool ApiKeyConfigured,
    DateTime? UpdatedAtUtc);
