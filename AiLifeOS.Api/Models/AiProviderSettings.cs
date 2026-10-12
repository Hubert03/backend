namespace AiLifeOS.Api.Models;

public sealed class AiProviderSettings
{
    public byte SettingsId { get; set; }
    public string ApiUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public byte[] EncryptedApiKey { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
}
