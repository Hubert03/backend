namespace AiLifeOS.Api.Services;

public static class OpenRouterSettingsPolicy
{
    public const string DefaultApiUrl =
        "https://openrouter.ai/api/v1/chat/completions";

    public const string DefaultModel = "openrouter/free";

    public static bool IsAllowedApiUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "openrouter.ai", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && uri.AbsolutePath == "/api/v1/chat/completions"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}

//using System.Security.Cryptography;
//using System.Text;

//namespace AiLifeOS.Api.Services;

//public static class OpenRouterSettingsPolicy
//{
//    public const string DefaultApiUrl = "https://openrouter.ai/api/v1/chat/completions";
//    public const string DefaultModel = "openrouter/free";

//    private static readonly byte[] ApiKeyEntropy = Encoding.UTF8.GetBytes("AiLifeOS.OpenRouter.ApiKey.v1");

//    public static byte[] ProtectApiKey(string apiKey)
//    {
//        if (!OperatingSystem.IsWindows())
//        {
//            throw new PlatformNotSupportedException("OpenRouter API keys are protected with Windows DPAPI.");
//        }

//        var keyBytes = Encoding.UTF8.GetBytes(apiKey);
//        try
//        {
//            return ProtectedData.Protect(keyBytes, ApiKeyEntropy, DataProtectionScope.CurrentUser);
//        }
//        finally
//        {
//            CryptographicOperations.ZeroMemory(keyBytes);
//        }
//    }

//    public static byte[] UnprotectApiKey(byte[] encryptedApiKey)
//    {
//        if (!OperatingSystem.IsWindows())
//        {
//            throw new PlatformNotSupportedException("OpenRouter API keys are protected with Windows DPAPI.");
//        }

//        return ProtectedData.Unprotect(encryptedApiKey, ApiKeyEntropy, DataProtectionScope.CurrentUser);
//    }

//    public static bool IsAllowedApiUrl(string value) =>
//        Uri.TryCreate(value, UriKind.Absolute, out var uri)
//        && uri.Scheme == Uri.UriSchemeHttps
//        && string.Equals(uri.Host, "openrouter.ai", StringComparison.OrdinalIgnoreCase)
//        && uri.IsDefaultPort
//        && uri.AbsolutePath == "/api/v1/chat/completions"
//        && string.IsNullOrEmpty(uri.Query)
//        && string.IsNullOrEmpty(uri.Fragment);
//}
