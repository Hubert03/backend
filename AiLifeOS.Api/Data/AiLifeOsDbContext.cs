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
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
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
            entity.HasOne<ChatGroup>()
                .WithMany()
                .HasForeignKey(message => message.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(message => message.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(message => new { message.GroupId, message.CreatedAtUtc });
        });
    }
}
