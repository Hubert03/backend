
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiLifeOS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Services;

public sealed class OpenRouterGateway(
    HttpClient httpClient,
    AiLifeOsDbContext dbContext,
    OpenRouterApiKeyProtector apiKeyProtector)
{
    public async Task<string> CompleteJsonAsync(
        string systemPrompt, string userContent, int maxTokens, CancellationToken cancellationToken)
    {
        var settings = await dbContext.AiProviderSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SettingsId == 1, cancellationToken);

        if (settings is null || settings.EncryptedApiKey is not { Length: > 0 })
        {
            throw new AiPlanConfigurationException(
                "AI is not configured. Configure the AI provider first.");
        }

        if (!OpenRouterSettingsPolicy.IsAllowedApiUrl(settings.ApiUrl)
            || string.IsNullOrWhiteSpace(settings.Model)
            || settings.Model.Length > 100)
        {
            throw new AiPlanConfigurationException("The stored OpenRouter URL or model is invalid.");
        }

        string apiKey;

        try
        {
            apiKey = apiKeyProtector.UnprotectApiKey(settings.EncryptedApiKey);
        }
        catch (CryptographicException exception)
        {
            throw new AiPlanConfigurationException(
                "The stored OpenRouter key cannot be decrypted. Update the provider settings in the app.",
                exception);
        }
        catch (FormatException exception)
        {
            throw new AiPlanConfigurationException(
                "The stored OpenRouter key has an invalid encrypted format. Update the provider settings in the app.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiPlanConfigurationException("The stored OpenRouter API key is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = settings.Model,
                temperature = 0.3,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiPlanProviderException(response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (body.Length > 1_000_000)
        {
            throw new AiPlanProviderException(failureMessage: "OpenRouter returned an oversized response.");
        }

        string? content;
        string? finishReason = null;
        string? refusal = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var choices = document.RootElement.GetProperty("choices");

            if (choices.GetArrayLength() == 0)
            {
                throw new AiPlanProviderException(failureMessage: "The AI returned no answer. Try again.");
            }

            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var reason)
                && reason.ValueKind == JsonValueKind.String)
            {
                finishReason = reason.GetString();
            }

            var message = choice.GetProperty("message");

            if (message.TryGetProperty("refusal", out var refusalElement)
                && refusalElement.ValueKind == JsonValueKind.String)
            {
                refusal = refusalElement.GetString();
            }

            content = ReadContent(message);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiPlanProviderException(failureMessage: "The AI returned an unsupported response format.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiPlanProviderException(failureMessage: finishReason switch
            {
                "length" => "The model ran out of output space before it wrote an answer. In Settings, choose a model without long reasoning, or try again.",
                _ when !string.IsNullOrWhiteSpace(refusal) => "The selected AI model declined this request. Try again or choose another model in Settings.",
                _ => $"The selected AI model returned no text (finish reason: {finishReason ?? "none"}). Choose a model that supports JSON output in Settings."
            });
        }

        if (content.Length > 100_000)
        {
            throw new AiPlanProviderException(failureMessage: "The AI response was too large to use. Try again.");
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            throw new AiPlanProviderException(failureMessage: "The AI did not return JSON. Choose a model that supports JSON output in Settings.");
        }

        return content[start..(end + 1)];
    }

    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return null;

        if (content.ValueKind == JsonValueKind.String) return content.GetString();

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String && type.GetString() == "text"
                    && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return string.Join("\n", parts);
        }

        return null;
    }
}
