namespace AiLifeOS.Api.Models;

public sealed class LoginHistoryEntry
{
    public Guid LoginHistoryId { get; set; }
    public Guid UserId { get; set; }
    public bool Succeeded { get; set; }
    public DateTime AttemptedAtUtc { get; set; }

    // Original fields
    public string? IpAddress { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceType { get; set; }
    public string? UserAgent { get; set; }      // ✅ Must exist
    public bool IsNewDevice { get; set; }       // ✅ Must exist

    // New fingerprint fields
    public string? Language { get; set; }
    public string? Timezone { get; set; }
    public string? ScreenResolution { get; set; }
    public int? ColorDepth { get; set; }
    public string? Platform { get; set; }
    public bool? CookiesEnabled { get; set; }
    public bool? TouchSupport { get; set; }
    public int? HardwareConcurrency { get; set; }
}
