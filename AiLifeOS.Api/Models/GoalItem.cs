namespace AiLifeOS.Api.Models;

public sealed class GoalItem
{
    public Guid GoalId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public byte Progress { get; set; }
    public DateOnly? TargetDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public ICollection<GoalMilestone> Milestones { get; set; } = new List<GoalMilestone>();
}
