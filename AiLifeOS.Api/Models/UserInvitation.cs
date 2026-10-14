namespace AiLifeOS.Api.Models;

public sealed class UserInvitation
{
    public long InvitationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte[] TokenHash { get; set; } = [];
    public Guid CreatedByUserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
