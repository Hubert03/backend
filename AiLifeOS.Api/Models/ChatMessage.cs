namespace AiLifeOS.Api.Models;

public sealed class ChatMessage
{
    public Guid MessageId { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
