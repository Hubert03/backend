namespace AiLifeOS.Api.Models;

public sealed class Team
{
    public Guid TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ManagerUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class TeamTask
{
    public Guid TaskId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Pending";
    public byte Priority { get; set; } = 2;
    public DateTime? DueAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public sealed class TeamGoal
{
    public Guid GoalId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public byte Progress { get; set; }
    public DateTime? TargetDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public List<TeamGoalMilestone> Milestones { get; set; } = [];
}

public sealed class TeamGoalMilestone
{
    public Guid GoalMilestoneId { get; set; }
    public Guid GoalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public short SortOrder { get; set; }
}

public sealed class TeamNote
{
    public Guid NoteId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Color { get; set; } = "#f3f4f6";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public sealed class ChatMessageMention
{
    public Guid MessageId { get; set; }
    public Guid MentionedUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class Reminder
{
    public Guid ReminderId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public string Status { get; set; } = "Scheduled";
    public DateTime? TriggeredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class PushSubscription
{
    public Guid PushSubscriptionId { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
