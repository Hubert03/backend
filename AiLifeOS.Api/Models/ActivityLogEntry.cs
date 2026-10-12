namespace AiLifeOS.Api.Models;

public sealed class ActivityLogEntry
{
    public Guid ActivityLogId { get; set; }
    public Guid UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
