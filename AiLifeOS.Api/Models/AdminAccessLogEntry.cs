namespace AiLifeOS.Api.Models;

public sealed class AdminAccessLogEntry
{
    public Guid AdminAccessLogId { get; set; }
    public Guid AdminUserId { get; set; }
    public string AdminDisplayName { get; set; } = string.Empty;
    public Guid TargetUserId { get; set; }
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}