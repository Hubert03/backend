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
public sealed class SettingsController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery) : ControllerBase
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
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

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
            return BadRequest(new { message = "The model name cannot be empty." });
        }

        if (apiKey?.Any(char.IsControl) == true)
        {
            return BadRequest(new { message = "The API key must not contain control characters." });
        }

        if (request.ClearApiKey && !string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { message = "Provide a new API key or clear the saved key, not both." });
        }

        if (!OperatingSystem.IsWindows())
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Saving OpenRouter keys is only supported on Windows because the key is protected with Windows DPAPI."
            });
        }

        var settings = await dbContext.AiProviderSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SettingsId == 1, cancellationToken);
        byte[] encryptedApiKey;
        if (request.ClearApiKey)
        {
            encryptedApiKey = [];
        }
        else if (!string.IsNullOrWhiteSpace(apiKey))
        {
            encryptedApiKey = OpenRouterSettingsPolicy.ProtectApiKey(apiKey);
        }
        else if (settings is not null && settings.EncryptedApiKey.Length > 0)
        {
            encryptedApiKey = settings.EncryptedApiKey;
        }
        else
        {
            return BadRequest(new { message = "Enter an API key before saving the provider settings." });
        }

        var updatedAtUtc = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.AiProviderSettings_Upsert @ApiUrl={apiUrl}, @Model={model}, @EncryptedApiKey={encryptedApiKey}, @UpdatedAtUtc={updatedAtUtc}",
            cancellationToken);
        var savedSettings = await dbContext.AiProviderSettings.AsNoTracking()
            .SingleAsync(item => item.SettingsId == 1, cancellationToken);
        return Ok(ToResponse(savedSettings));
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
