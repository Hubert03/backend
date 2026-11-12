namespace AiLifeOS.Api.Models;

public sealed class ReminderPushDelivery
{
    public Guid ReminderPushDeliveryId { get; set; }
    public Guid ReminderId { get; set; }
    public Guid PushSubscriptionId { get; set; }
    public DateTime DueAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
