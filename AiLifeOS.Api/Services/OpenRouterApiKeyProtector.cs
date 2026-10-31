using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AiLifeOS.Api.Services;

public sealed class OpenRouterApiKeyProtector
{
    private readonly IDataProtector _protector;

    public OpenRouterApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(
            "AiLifeOS.OpenRouter.ApiKey.v1");
    }

    public byte[] ProtectApiKey(string apiKey)
    {
        var protectedKey = _protector.Protect(apiKey);
        return Encoding.UTF8.GetBytes(protectedKey);
    }

    public string UnprotectApiKey(byte[] encryptedApiKey)
    {
        var protectedKey = Encoding.UTF8.GetString(encryptedApiKey);
        return _protector.Unprotect(protectedKey);
    }
}