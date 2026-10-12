namespace AiLifeOS.Api.Models;

public sealed class GoalMilestone
{
    public Guid GoalMilestoneId { get; set; }
    public Guid GoalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public short SortOrder { get; set; }
    public GoalItem Goal { get; set; } = null!;
}
