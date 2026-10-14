namespace AiLifeOS.Api.Models;

public sealed class ChatGroup
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsArchived { get; set; }
}
