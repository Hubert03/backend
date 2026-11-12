using AiLifeOS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Services;

//public static class OpenRouterSettingsConfigurator
//{
//    public static async Task ConfigureAsync(AiLifeOsDbContext dbContext)
//    {
public static class OpenRouterSettingsConfigurator
{
    public static async Task ConfigureAsync(AiLifeOsDbContext dbContext,OpenRouterApiKeyProtector apiKeyProtector)
    { 
        //if (!OperatingSystem.IsWindows())
        //{
        //    throw new PlatformNotSupportedException(
        //        "The local OpenRouter settings command uses Windows DPAPI and must run on Windows.");
        //}

        Console.Write($"OpenRouter API URL [{OpenRouterSettingsPolicy.DefaultApiUrl}]: ");
        var apiUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            apiUrl = OpenRouterSettingsPolicy.DefaultApiUrl;
        }

        if (!OpenRouterSettingsPolicy.IsAllowedApiUrl(apiUrl))
        {
            throw new InvalidOperationException(
                "The API URL must be the HTTPS OpenRouter chat completions endpoint.");
        }

        Console.Write($"OpenRouter model [{OpenRouterSettingsPolicy.DefaultModel}]: ");
        var model = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(model))
        {
            model = OpenRouterSettingsPolicy.DefaultModel;
        }
        model = model.Trim();
        if (model.Length > 100)
        {
            throw new InvalidOperationException("The model name cannot exceed 100 characters.");
        }

        var apiKey = ReadHidden("OpenRouter API key (input hidden): ");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("The API key cannot be empty.");
        }

        var encryptedKey = apiKeyProtector.ProtectApiKey(apiKey);

        var updatedAtUtc = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.AiProviderSettings_Upsert @ApiUrl={apiUrl}, @Model={model}, @EncryptedApiKey={encryptedKey}, @UpdatedAtUtc={updatedAtUtc}");

        Console.WriteLine("OpenRouter settings saved to the AiLifeOS database.");
        Console.WriteLine("The API key was encrypted using ASP.NET Core Data Protection.");
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                characters.Add(key.KeyChar);
            }
        }
    }

}
