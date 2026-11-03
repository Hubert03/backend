using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Data;

public sealed class AiLifeOsDbContext(DbContextOptions<AiLifeOsDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<User> UserProfiles => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<GoalItem> Goals => Set<GoalItem>();
    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();
    public DbSet<NoteItem> Notes => Set<NoteItem>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();
    public DbSet<AiProviderSettings> AiProviderSettings => Set<AiProviderSettings>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
    public DbSet<ChatGroupMember> ChatGroupMembers => Set<ChatGroupMember>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AdminAccessLogEntry> AdminAccessLog => Set<AdminAccessLogEntry>();
    public DbSet<LoginHistoryEntry> LoginHistory => Set<LoginHistoryEntry>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamTask> TeamTasks => Set<TeamTask>();
    public DbSet<TeamGoal> TeamGoals => Set<TeamGoal>();
    public DbSet<TeamGoalMilestone> TeamGoalMilestones => Set<TeamGoalMilestone>();
    public DbSet<TeamNote> TeamNotes => Set<TeamNote>();
    public DbSet<ChatMessageMention> ChatMessageMentions => Set<ChatMessageMention>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<ReminderPushDelivery> ReminderPushDeliveries => Set<ReminderPushDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(user => user.IsAdmin).HasDefaultValue(false);
            entity.Property(user => user.IsActive).HasDefaultValue(true);
            entity.Property(user => user.MustChangePassword).HasDefaultValue(false);
            entity.Property(user => user.AccessRole).HasMaxLength(24).HasDefaultValue("User").IsRequired();
            entity.Property(user => user.TeamId);
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
            entity.HasOne<Team>()
                .WithMany()
                .HasForeignKey(user => user.TeamId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users", "dbo");
            entity.HasKey(user => user.UserId);
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(user => user.Email)
                .IsUnique()
                .HasDatabaseName("UQ_Users_Email");
            entity.HasOne<ApplicationUser>()
                .WithOne()
                .HasForeignKey<User>(user => user.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("Tasks", "dbo");
            entity.HasKey(task => task.TaskId);
            entity.Property(task => task.TaskId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(task => task.Title).HasMaxLength(200).IsRequired();
            entity.Property(task => task.Description).HasMaxLength(2000);
            entity.Property(task => task.Status).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(task => task.Priority).HasDefaultValue((byte)2);
            entity.Property(task => task.DueAtUtc).HasPrecision(0);
            entity.Property(task => task.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(task => task.UpdatedAtUtc).HasPrecision(0);
            entity.Property(task => task.IsDeleted).HasDefaultValue(false);
            entity.Property(task => task.DeletedAtUtc).HasPrecision(0);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(task => task.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(task => new { task.UserId, task.Status, task.DueAtUtc })
                .HasDatabaseName("IX_Tasks_UserId_Status_DueAtUtc");
            entity.HasIndex(task => new { task.UserId, task.IsDeleted, task.DeletedAtUtc })
                .HasDatabaseName("IX_Tasks_UserId_IsDeleted_DeletedAtUtc");
        });

        modelBuilder.Entity<GoalItem>(entity =>
        {
            entity.ToTable("Goals", "dbo");
            entity.HasKey(goal => goal.GoalId);
            entity.Property(goal => goal.GoalId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(goal => goal.Title).HasMaxLength(200).IsRequired();
            entity.Property(goal => goal.Description).HasMaxLength(2000);
            entity.Property(goal => goal.Status).HasMaxLength(20).HasDefaultValue("Active");
            entity.Property(goal => goal.Progress).HasDefaultValue((byte)0);
            entity.Property(goal => goal.TargetDate).HasColumnType("date");
            entity.Property(goal => goal.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(goal => goal.UpdatedAtUtc).HasPrecision(0);
            entity.Property(goal => goal.IsDeleted).HasDefaultValue(false);
            entity.Property(goal => goal.DeletedAtUtc).HasPrecision(0);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(goal => goal.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(goal => goal.Milestones)
                .WithOne(milestone => milestone.Goal)
                .HasForeignKey(milestone => milestone.GoalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(goal => new { goal.UserId, goal.Status, goal.TargetDate })
                .HasDatabaseName("IX_Goals_UserId_Status_TargetDate");
            entity.HasIndex(goal => new { goal.UserId, goal.IsDeleted, goal.DeletedAtUtc })
                .HasDatabaseName("IX_Goals_UserId_IsDeleted_DeletedAtUtc");
        });

        modelBuilder.Entity<GoalMilestone>(entity =>
        {
            entity.ToTable("GoalMilestones", "dbo");
            entity.HasKey(milestone => milestone.GoalMilestoneId);
            entity.Property(milestone => milestone.GoalMilestoneId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(milestone => milestone.Title).HasMaxLength(200).IsRequired();
            entity.Property(milestone => milestone.SortOrder).HasDefaultValue((short)0);
            entity.Property(milestone => milestone.IsCompleted).HasDefaultValue(false);
        });

        modelBuilder.Entity<NoteItem>(entity =>
        {
            entity.ToTable("Notes", "dbo");
            entity.HasKey(note => note.NoteId);
            entity.Property(note => note.NoteId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(note => note.Title).HasMaxLength(200).IsRequired();
            entity.Property(note => note.Content).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(note => note.Color).HasMaxLength(20).HasDefaultValue("#f3f4f6").IsRequired();
            entity.Property(note => note.IsPinned).HasDefaultValue(false);
            entity.Property(note => note.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(note => note.UpdatedAtUtc).HasPrecision(0);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(note => note.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(note => new { note.UserId, note.IsPinned, note.UpdatedAtUtc })
                .HasDatabaseName("IX_Notes_UserId_IsPinned_UpdatedAtUtc");
        });

        modelBuilder.Entity<ActivityLogEntry>(entity =>
        {
            entity.ToTable("ActivityLog", "dbo");
            entity.HasKey(activity => activity.ActivityLogId);
            entity.Property(activity => activity.ActivityLogId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(activity => activity.EntityType).HasMaxLength(20).IsRequired();
            entity.Property(activity => activity.Action).HasMaxLength(20).IsRequired();
            entity.Property(activity => activity.Title).HasMaxLength(500).IsRequired();
            entity.Property(activity => activity.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(activity => activity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(activity => new { activity.UserId, activity.CreatedAtUtc })
                .HasDatabaseName("IX_ActivityLog_UserId_CreatedAtUtc");
        });

        modelBuilder.Entity<AiProviderSettings>(entity =>
        {
            entity.ToTable("AiProviderSettings", "dbo");
            entity.HasKey(settings => settings.SettingsId);
            entity.Property(settings => settings.SettingsId).ValueGeneratedNever();
            entity.Property(settings => settings.ApiUrl).HasMaxLength(500).IsRequired();
            entity.Property(settings => settings.Model).HasMaxLength(100).IsRequired();
            entity.Property(settings => settings.EncryptedApiKey).IsRequired();
            entity.Property(settings => settings.UpdatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AiProviderSettings_SingleRow",
                "[SettingsId] = 1"));
        });

        modelBuilder.Entity<UserInvitation>(entity =>
        {
            entity.ToTable("UserInvitations", "dbo");
            entity.HasKey(invitation => invitation.InvitationId);
            entity.Property(invitation => invitation.Email).HasMaxLength(320).IsRequired();
            entity.Property(invitation => invitation.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(32).IsRequired();
            entity.Property(invitation => invitation.CreatedAtUtc).HasPrecision(0);
            entity.Property(invitation => invitation.ExpiresAtUtc).HasPrecision(0);
            entity.Property(invitation => invitation.AcceptedAtUtc).HasPrecision(0);
            entity.HasIndex(invitation => invitation.TokenHash).IsUnique();
            entity.HasIndex(invitation => new { invitation.Email, invitation.AcceptedAtUtc });
        });

        modelBuilder.Entity<ChatGroup>(entity =>
        {
            entity.ToTable("ChatGroups", "dbo");
            entity.HasKey(group => group.GroupId);
            entity.Property(group => group.Name).HasMaxLength(100).IsRequired();
            entity.Property(group => group.CreatedAtUtc).HasPrecision(0);
            entity.Property(group => group.IsArchived).HasDefaultValue(false);
            entity.HasIndex(group => new { group.IsArchived, group.Name });
        });

        modelBuilder.Entity<ChatGroupMember>(entity =>
        {
            entity.ToTable("ChatGroupMembers", "dbo");
            entity.HasKey(member => new { member.GroupId, member.UserId });
            entity.Property(member => member.AddedAtUtc).HasPrecision(0);
            entity.HasOne<ChatGroup>()
                .WithMany()
                .HasForeignKey(member => member.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(member => new { member.UserId, member.GroupId });
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages", "dbo");
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.Content).HasMaxLength(2000).IsRequired();
            entity.Property(message => message.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(message => message.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(message => message.IsDeleted).HasDefaultValue(false);
            entity.Property(message => message.ReplyToMessageId);
            entity.Property(message => message.DeletedByUserId);
            entity.HasOne<ChatGroup>()
                .WithMany()
                .HasForeignKey(message => message.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(message => message.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(message => new { message.GroupId, message.CreatedAtUtc });
            entity.HasOne<ChatMessage>()
                .WithMany()
                .HasForeignKey(message => message.ReplyToMessageId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(message => message.DeletedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AdminAccessLogEntry>(entity =>
        {
            entity.ToTable("AdminAccessLog", "dbo");
            entity.HasKey(log => log.AdminAccessLogId);
            entity.Property(log => log.AdminAccessLogId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(log => log.AdminDisplayName).HasMaxLength(100).IsRequired();
            entity.Property(log => log.TargetDisplayName).HasMaxLength(100).IsRequired();
            entity.Property(log => log.Resource).HasMaxLength(20).IsRequired();
            entity.Property(log => log.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<LoginHistoryEntry>(entity =>
        {
            entity.ToTable("LoginHistory", "dbo");
            entity.HasKey(entry => entry.LoginHistoryId);
            entity.Property(entry => entry.LoginHistoryId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(entry => entry.AttemptedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(entry => entry.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(entry => new { entry.UserId, entry.AttemptedAtUtc })
                .HasDatabaseName("IX_LoginHistory_UserId_AttemptedAtUtc");
            entity.HasIndex(entry => entry.AttemptedAtUtc)
                .HasDatabaseName("IX_LoginHistory_AttemptedAtUtc");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("Teams", "dbo");
            entity.HasKey(team => team.TeamId);
            entity.Property(team => team.TeamId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(team => team.Name).HasMaxLength(100).IsRequired();
            entity.Property(team => team.CreatedAtUtc)
                .HasPrecision(0)
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(team => team.Name).IsUnique().HasDatabaseName("UX_Teams_Name");
            entity.HasIndex(team => team.ManagerUserId)
                .IsUnique()
                .HasFilter("[ManagerUserId] IS NOT NULL")
                .HasDatabaseName("UX_Teams_ManagerUserId");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(team => team.ManagerUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(team => team.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TeamTask>(entity =>
        {
            entity.ToTable("TeamTasks", "dbo");
            entity.HasKey(task => task.TaskId);
            entity.Property(task => task.TaskId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(task => task.Title).HasMaxLength(200).IsRequired();
            entity.Property(task => task.Description).HasMaxLength(2000);
            entity.Property(task => task.Status).HasMaxLength(20).HasDefaultValue("Pending").IsRequired();
            entity.Property(task => task.Priority).HasDefaultValue((byte)2);
            entity.Property(task => task.DueAtUtc).HasPrecision(0);
            entity.Property(task => task.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(task => task.UpdatedAtUtc).HasPrecision(0);
            entity.Property(task => task.IsDeleted).HasDefaultValue(false);
            entity.Property(task => task.DeletedAtUtc).HasPrecision(0);
            entity.HasOne<Team>().WithMany().HasForeignKey(task => task.TeamId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(task => task.AssignedToUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(task => new { task.TeamId, task.IsDeleted, task.DueAtUtc })
                .HasDatabaseName("IX_TeamTasks_TeamId_IsDeleted_DueAtUtc");
        });

        modelBuilder.Entity<TeamGoal>(entity =>
        {
            entity.ToTable("TeamGoals", "dbo");
            entity.HasKey(goal => goal.GoalId);
            entity.Property(goal => goal.GoalId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(goal => goal.Title).HasMaxLength(200).IsRequired();
            entity.Property(goal => goal.Description).HasMaxLength(2000);
            entity.Property(goal => goal.Status).HasMaxLength(20).HasDefaultValue("Active").IsRequired();
            entity.Property(goal => goal.Progress).HasDefaultValue((byte)0);
            entity.Property(goal => goal.TargetDate).HasColumnType("date");
            entity.Property(goal => goal.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(goal => goal.UpdatedAtUtc).HasPrecision(0);
            entity.Property(goal => goal.IsDeleted).HasDefaultValue(false);
            entity.Property(goal => goal.DeletedAtUtc).HasPrecision(0);
            entity.HasOne<Team>().WithMany().HasForeignKey(goal => goal.TeamId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(goal => goal.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(goal => goal.Milestones)
                .WithOne()
                .HasForeignKey(milestone => milestone.GoalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(goal => new { goal.TeamId, goal.IsDeleted, goal.TargetDate })
                .HasDatabaseName("IX_TeamGoals_TeamId_IsDeleted_TargetDate");
        });

        modelBuilder.Entity<TeamGoalMilestone>(entity =>
        {
            entity.ToTable("TeamGoalMilestones", "dbo");
            entity.HasKey(milestone => milestone.GoalMilestoneId);
            entity.Property(milestone => milestone.GoalMilestoneId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(milestone => milestone.Title).HasMaxLength(200).IsRequired();
            entity.Property(milestone => milestone.IsCompleted).HasDefaultValue(false);
            entity.Property(milestone => milestone.SortOrder).HasDefaultValue((short)0);
            entity.HasIndex(milestone => new { milestone.GoalId, milestone.SortOrder })
                .HasDatabaseName("IX_TeamGoalMilestones_GoalId_SortOrder");
        });

        modelBuilder.Entity<TeamNote>(entity =>
        {
            entity.ToTable("TeamNotes", "dbo");
            entity.HasKey(note => note.NoteId);
            entity.Property(note => note.NoteId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(note => note.Title).HasMaxLength(200).IsRequired();
            entity.Property(note => note.Content).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(note => note.Color).HasMaxLength(7).HasDefaultValue("#f3f4f6").IsRequired();
            entity.Property(note => note.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(note => note.UpdatedAtUtc).HasPrecision(0);
            entity.Property(note => note.IsDeleted).HasDefaultValue(false);
            entity.Property(note => note.DeletedAtUtc).HasPrecision(0);
            entity.HasOne<Team>().WithMany().HasForeignKey(note => note.TeamId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(note => note.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(note => new { note.TeamId, note.IsDeleted, note.CreatedAtUtc })
                .HasDatabaseName("IX_TeamNotes_TeamId_IsDeleted_CreatedAtUtc");
        });

        modelBuilder.Entity<ChatMessageMention>(entity =>
        {
            entity.ToTable("ChatMessageMentions", "dbo");
            entity.HasKey(mention => new { mention.MessageId, mention.MentionedUserId });
            entity.Property(mention => mention.DisplayName).HasMaxLength(100).IsRequired();
            entity.HasOne<ChatMessage>().WithMany().HasForeignKey(mention => mention.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(mention => mention.MentionedUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(mention => new { mention.MentionedUserId, mention.MessageId })
                .HasDatabaseName("IX_ChatMessageMentions_MentionedUserId");
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("Reminders", "dbo");
            entity.HasKey(reminder => reminder.ReminderId);
            entity.Property(reminder => reminder.ReminderId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(reminder => reminder.Title).HasMaxLength(200).IsRequired();
            entity.Property(reminder => reminder.DueAtUtc).HasPrecision(0);
            entity.Property(reminder => reminder.Status).HasMaxLength(16).HasDefaultValue("Scheduled").IsRequired();
            entity.Property(reminder => reminder.TriggeredAtUtc).HasPrecision(0);
            entity.Property(reminder => reminder.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(reminder => reminder.UpdatedAtUtc).HasPrecision(0);
            entity.HasOne<User>().WithMany().HasForeignKey(reminder => reminder.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TaskItem>().WithMany().HasForeignKey(reminder => reminder.TaskId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(reminder => new { reminder.Status, reminder.DueAtUtc })
                .HasDatabaseName("IX_Reminders_Status_DueAtUtc");
            entity.HasIndex(reminder => new { reminder.UserId, reminder.Status, reminder.DueAtUtc })
                .HasDatabaseName("IX_Reminders_UserId_Status_DueAtUtc");
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("PushSubscriptions", "dbo");
            entity.HasKey(subscription => subscription.PushSubscriptionId);
            entity.Property(subscription => subscription.PushSubscriptionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(subscription => subscription.Endpoint).HasMaxLength(2048).IsRequired();
            entity.Property(subscription => subscription.P256dh).HasMaxLength(256).IsRequired();
            entity.Property(subscription => subscription.Auth).HasMaxLength(256).IsRequired();
            entity.Property(subscription => subscription.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<User>().WithMany().HasForeignKey(subscription => subscription.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(subscription => subscription.UserId)
                .HasDatabaseName("IX_PushSubscriptions_UserId");
        });

        modelBuilder.Entity<ReminderPushDelivery>(entity =>
        {
            entity.ToTable("ReminderPushDeliveries", "dbo");
            entity.HasKey(delivery => delivery.ReminderPushDeliveryId);
            entity.Property(delivery => delivery.ReminderPushDeliveryId)
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(delivery => delivery.DueAtUtc).HasPrecision(0);
            entity.Property(delivery => delivery.Status).HasMaxLength(16).HasDefaultValue("Pending").IsRequired();
            entity.Property(delivery => delivery.AttemptCount).HasDefaultValue(0);
            entity.Property(delivery => delivery.NextAttemptAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(delivery => delivery.LastAttemptAtUtc).HasPrecision(0);
            entity.Property(delivery => delivery.LeaseUntilUtc).HasPrecision(0);
            entity.Property(delivery => delivery.SentAtUtc).HasPrecision(0);
            entity.Property(delivery => delivery.LastError).HasMaxLength(1000);
            entity.Property(delivery => delivery.CreatedAtUtc).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<Reminder>().WithMany()
                .HasForeignKey(delivery => delivery.ReminderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PushSubscription>().WithMany()
                .HasForeignKey(delivery => delivery.PushSubscriptionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(delivery => new
                {
                    delivery.ReminderId,
                    delivery.PushSubscriptionId,
                    delivery.DueAtUtc,
                })
                .IsUnique()
                .HasDatabaseName("UX_ReminderPushDeliveries_Reminder_Subscription_DueAtUtc");
            entity.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAtUtc })
                .HasDatabaseName("IX_ReminderPushDeliveries_Status_NextAttemptAtUtc");
            entity.HasIndex(delivery => new { delivery.Status, delivery.LeaseUntilUtc })
                .HasDatabaseName("IX_ReminderPushDeliveries_Status_LeaseUntilUtc");
        });
    }
}
