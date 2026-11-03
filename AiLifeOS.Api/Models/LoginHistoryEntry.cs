namespace AiLifeOS.Api.Models;

public sealed class LoginHistoryEntry
{
    public Guid LoginHistoryId { get; set; }
    public Guid UserId { get; set; }
    public bool Succeeded { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}
