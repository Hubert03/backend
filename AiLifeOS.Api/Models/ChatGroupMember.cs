namespace AiLifeOS.Api.Models;

public sealed class ChatGroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AddedAtUtc { get; set; }
}
