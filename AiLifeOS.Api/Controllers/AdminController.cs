using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using AiLifeOS.Api.Services;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public sealed class AdminController(
    AiLifeOsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IInvitationEmailSender invitationEmailSender,
    IOptions<InvitationEmailOptions> emailOptions,
    ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Administrator {AdminUserId} viewed the organization dashboard.",
            User.FindFirstValue(ClaimTypes.NameIdentifier));
        var todayUtc = DateTime.UtcNow.Date;
        var activityStartUtc = todayUtc.AddDays(-6);
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .Select(user => new
            {
                user.Id,
                Email = user.Email ?? string.Empty,
                user.DisplayName,
                user.IsAdmin,
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var taskCounts = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted)
            .GroupBy(task => task.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Total = group.Count(),
                Open = group.Count(task => task.Status == "Pending" || task.Status == "InProgress"),
                Completed = group.Count(task => task.Status == "Completed"),
                Overdue = group.Count(task =>
                    task.DueAtUtc != null &&
                    task.DueAtUtc < todayUtc &&
                    task.Status != "Completed" &&
                    task.Status != "Cancelled"),
            })
            .ToDictionaryAsync(count => count.UserId, cancellationToken);
        var goalCounts = await dbContext.Goals
            .AsNoTracking()
            .Where(goal => !goal.IsDeleted)
            .GroupBy(goal => goal.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Total = group.Count(),
                Active = group.Count(goal => goal.Status == "Active"),
                Completed = group.Count(goal => goal.Status == "Completed"),
            })
            .ToDictionaryAsync(count => count.UserId, cancellationToken);
        var noteCounts = await dbContext.Notes
            .AsNoTracking()
            .GroupBy(note => note.UserId)
            .Select(group => new { UserId = group.Key, Total = group.Count() })
            .ToDictionaryAsync(count => count.UserId, cancellationToken);
        var lastActivity = await dbContext.ActivityLog
            .AsNoTracking()
            .GroupBy(activity => activity.UserId)
            .Select(group => new { UserId = group.Key, LastActivityAtUtc = group.Max(activity => activity.CreatedAtUtc) })
            .ToDictionaryAsync(activity => activity.UserId, activity => (DateTime?)activity.LastActivityAtUtc, cancellationToken);
        var activityCounts = await dbContext.ActivityLog
            .AsNoTracking()
            .Where(activity => activity.CreatedAtUtc >= activityStartUtc)
            .GroupBy(activity => activity.CreatedAtUtc.Date)
            .Select(group => new { DateUtc = group.Key, Total = group.Count() })
            .ToDictionaryAsync(day => day.DateUtc, day => day.Total, cancellationToken);
        var taskStatusCounts = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted)
            .GroupBy(task => task.Status)
            .Select(group => new { Status = group.Key, Total = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Total, cancellationToken);
        var visibleGoals = dbContext.Goals
            .AsNoTracking()
            .Where(goal => !goal.IsDeleted);
        var activeGoals = await visibleGoals.CountAsync(goal => goal.Status == "Active", cancellationToken);
        var completedGoals = await visibleGoals.CountAsync(goal => goal.Status == "Completed", cancellationToken);

        var dashboardUsers = users
            .Select(user =>
            {
                taskCounts.TryGetValue(user.Id, out var tasks);
                goalCounts.TryGetValue(user.Id, out var goals);
                noteCounts.TryGetValue(user.Id, out var notes);
                lastActivity.TryGetValue(user.Id, out var lastActivityAtUtc);
                return new AdminWorkspaceSummaryResponse(
                    user.Id,
                    user.Email,
                    user.DisplayName,
                    user.IsAdmin,
                    user.IsActive,
                    user.MustChangePassword,
                    user.CreatedAtUtc,
                    tasks?.Total ?? 0,
                    tasks?.Open ?? 0,
                    tasks?.Completed ?? 0,
                    tasks?.Overdue ?? 0,
                    goals?.Total ?? 0,
                    goals?.Active ?? 0,
                    goals?.Completed ?? 0,
                    notes?.Total ?? 0,
                    lastActivityAtUtc);
            })
            .ToArray();
        var activityByDay = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var dateUtc = activityStartUtc.AddDays(offset);
                return new AdminActivityDayResponse(
                    dateUtc,
                    activityCounts.GetValueOrDefault(dateUtc));
            })
            .ToArray();
        var taskStatuses = new[] { "Pending", "InProgress", "Completed", "Cancelled" }
            .Select(status => new AdminTaskStatusCountResponse(
                status,
                taskStatusCounts.GetValueOrDefault(status)))
            .ToArray();

        return Ok(new AdminDashboardResponse(
            users.Count,
            users.Count(user => user.IsActive),
            users.Count(user => !user.IsActive),
            users.Count(user => user.IsAdmin && user.IsActive),
            users.Count(user => user.MustChangePassword),
            taskCounts.Values.Sum(count => count.Total),
            taskCounts.Values.Sum(count => count.Open),
            taskCounts.Values.Sum(count => count.Completed),
            taskCounts.Values.Sum(count => count.Overdue),
            activeGoals,
            completedGoals,
            noteCounts.Values.Sum(count => count.Total),
            taskStatuses,
            activityByDay,
            dashboardUsers));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .Select(user => new AdminUserResponse(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                user.IsAdmin,
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("users/{userId:guid}/workspace/tasks")]
    public async Task<ActionResult<AdminPageResponse<AdminTaskResponse>>> GetUserTasks(
        Guid userId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
            return NotFound();
        if (!IsValidPage(skip, take))
            return BadRequest(new { message = "Skip must be non-negative and take must be between 1 and 100." });
        AuditWorkspaceRead(userId, "tasks", skip, take);

        var query = dbContext.Tasks.AsNoTracking().Where(task => task.UserId == userId);
        var items = await query
            .OrderByDescending(task => task.CreatedAtUtc)
            .ThenBy(task => task.TaskId)
            .Skip(skip)
            .Take(take)
            .Select(task => new AdminTaskResponse(
                task.TaskId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueAtUtc,
                task.CreatedAtUtc,
                task.UpdatedAtUtc,
                task.IsDeleted))
            .ToListAsync(cancellationToken);
        return Ok(new AdminPageResponse<AdminTaskResponse>(
            items,
            skip,
            take,
            await query.CountAsync(cancellationToken)));
    }

    [HttpGet("users/{userId:guid}/workspace/goals")]
    public async Task<ActionResult<AdminPageResponse<AdminGoalResponse>>> GetUserGoals(
        Guid userId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
            return NotFound();
        if (!IsValidPage(skip, take))
            return BadRequest(new { message = "Skip must be non-negative and take must be between 1 and 100." });
        AuditWorkspaceRead(userId, "goals", skip, take);

        var query = dbContext.Goals
            .AsNoTracking()
            .Include(goal => goal.Milestones)
            .Where(goal => goal.UserId == userId);
        var goals = await query
            .OrderByDescending(goal => goal.CreatedAtUtc)
            .ThenBy(goal => goal.GoalId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        var items = goals.Select(goal => new AdminGoalResponse(
            goal.GoalId,
            goal.Title,
            goal.Description,
            goal.Status,
            goal.Progress,
            goal.TargetDate,
            goal.CreatedAtUtc,
            goal.UpdatedAtUtc,
            goal.IsDeleted,
            goal.Milestones
                .OrderBy(milestone => milestone.SortOrder)
                .Select(milestone => new AdminGoalMilestoneResponse(
                    milestone.Title,
                    milestone.IsCompleted,
                    milestone.SortOrder))
                .ToArray())).ToArray();
        return Ok(new AdminPageResponse<AdminGoalResponse>(
            items,
            skip,
            take,
            await query.CountAsync(cancellationToken)));
    }

    [HttpGet("users/{userId:guid}/workspace/notes")]
    public async Task<ActionResult<AdminPageResponse<AdminNoteResponse>>> GetUserNotes(
        Guid userId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
            return NotFound();
        if (!IsValidPage(skip, take))
            return BadRequest(new { message = "Skip must be non-negative and take must be between 1 and 100." });
        AuditWorkspaceRead(userId, "notes", skip, take);

        var query = dbContext.Notes.AsNoTracking().Where(note => note.UserId == userId);
        var items = await query
            .OrderByDescending(note => note.UpdatedAtUtc ?? note.CreatedAtUtc)
            .ThenBy(note => note.NoteId)
            .Skip(skip)
            .Take(take)
            .Select(note => new AdminNoteResponse(
                note.NoteId,
                note.Title,
                note.Content,
                note.Color,
                note.IsPinned,
                note.CreatedAtUtc,
                note.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new AdminPageResponse<AdminNoteResponse>(
            items,
            skip,
            take,
            await query.CountAsync(cancellationToken)));
    }

    [HttpPost("users")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { message = "An account with this email already exists." });

        var temporaryPassword =
            $"Aa1!{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18))}z9";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            MustChangePassword = true,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await userManager.CreateAsync(user, temporaryPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserProfile_Upsert @UserId={user.Id}, @Email={email}, @DisplayName={user.DisplayName}, @CreatedAtUtc={user.CreatedAtUtc}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedUserResponse(
                user.Id,
                email,
                user.DisplayName,
                temporaryPassword,
                user.MustChangePassword));
    }

    [HttpPost("invitations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvitation(
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { message = "An account with this email already exists." });

        var settings = emailOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(settings.FromAddress) ||
            string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ||
            settings.SmtpPort is < 1 or > 65535 ||
            !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            publicBaseUri.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(publicBaseUri.Query) ||
            !string.IsNullOrEmpty(publicBaseUri.Fragment) ||
            !MailAddress.TryCreate(settings.FromAddress, out _))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Invitation email is not configured",
                detail: "Configure Email:SmtpHost, Email:FromAddress, and Email:PublicBaseUrl before sending invitations.");
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var expiresAtUtc = DateTime.UtcNow.AddHours(48);
        var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserInvitation_Create @Email={email}, @DisplayName={request.DisplayName.Trim()}, @TokenHash={tokenHash}, @CreatedByUserId={adminId}, @ExpiresAtUtc={expiresAtUtc}",
            cancellationToken);

        var invitationUrl =
            $"{settings.PublicBaseUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(token)}";
        try
        {
            await invitationEmailSender.SendInvitationAsync(
                email,
                request.DisplayName.Trim(),
                invitationUrl,
                cancellationToken);
        }
        catch (SmtpException exception)
        {
            logger.LogError(exception, "Could not send invitation email to {Email}.", email);
            try
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC dbo.UserInvitation_Revoke @TokenHash={tokenHash}",
                    CancellationToken.None);
            }
            catch (SqlException revokeException)
            {
                logger.LogCritical(
                    revokeException,
                    "Could not revoke an undelivered invitation for {Email}.",
                    email);
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Invitation email failed and its token could not be invalidated",
                    detail: "Contact an administrator before retrying this invitation.");
            }

            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Invitation email could not be delivered",
                detail: "The incomplete invitation was invalidated. Check the SMTP configuration and try again.");
        }

        return Accepted(new InvitationCreatedResponse(email, expiresAtUtc));
    }

    [HttpPatch("users/{userId:guid}/access")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetUserAccess(
        Guid userId,
        SetUserAccessRequest request,
        CancellationToken cancellationToken)
    {
        var currentAdminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (userId == currentAdminId && (!request.IsAdmin || !request.IsActive))
            return BadRequest(new { message = "You cannot remove your own active administrator access." });

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return NotFound();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserAdmin_SetAccess @UserId={user.Id}, @IsAdmin={request.IsAdmin}, @IsActive={request.IsActive}, @MustChangePassword={user.MustChangePassword}",
            cancellationToken);
        return NoContent();
    }

    [HttpGet("chat-groups")]
    public async Task<IActionResult> GetChatGroups(CancellationToken cancellationToken)
    {
        var groups = await dbContext.ChatGroups
            .AsNoTracking()
            .OrderBy(group => group.Name)
            .Select(group => new
            {
                group.GroupId,
                group.Name,
                group.CreatedAtUtc,
                group.IsArchived,
            })
            .ToListAsync(cancellationToken);
        var groupIds = groups.Select(group => group.GroupId).ToArray();
        var memberships = await dbContext.ChatGroupMembers
            .AsNoTracking()
            .Where(member => groupIds.Contains(member.GroupId))
            .ToListAsync(cancellationToken);
        var membersByGroup = memberships
            .GroupBy(member => member.GroupId)
            .ToDictionary(group => group.Key, group => group.Select(member => member.UserId).ToList());
        var result = groups
            .Select(group => new AdminChatGroupResponse(
                group.GroupId,
                group.Name,
                group.CreatedAtUtc,
                group.IsArchived,
                membersByGroup.GetValueOrDefault(group.GroupId, [])))
            .ToList();

        return Ok(result);
    }

    [HttpPost("chat-groups")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateChatGroup(
        CreateChatGroupRequest request,
        CancellationToken cancellationToken)
    {
        var memberIds = request.MemberIds.Distinct().ToArray();
        var activeUsers = await userManager.Users
            .Where(user => user.IsActive && memberIds.Contains(user.Id))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        if (activeUsers.Count != memberIds.Length)
            return BadRequest(new { message = "Every selected member must be an active account." });

        var groupId = Guid.NewGuid();
        var creatorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatGroup_Create @GroupId={groupId}, @Name={request.Name.Trim()}, @CreatedByUserId={creatorId}",
            cancellationToken);

        foreach (var memberId in memberIds.Where(id => id != creatorId))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC dbo.ChatGroupMember_Add @GroupId={groupId}, @UserId={memberId}",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            new { groupId, name = request.Name.Trim() });
    }

    [HttpPost("chat-groups/{groupId:guid}/members")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGroupMember(
        Guid groupId,
        ChangeChatGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (!await userManager.Users.AnyAsync(
                user => user.Id == request.UserId && user.IsActive,
                cancellationToken))
            return BadRequest(new { message = "Only active accounts can be added to a chat group." });

        if (!await dbContext.ChatGroups.AnyAsync(
                group => group.GroupId == groupId && !group.IsArchived,
                cancellationToken))
            return NotFound();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatGroupMember_Add @GroupId={groupId}, @UserId={request.UserId}",
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("chat-groups/{groupId:guid}/members/{userId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGroupMember(
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatGroupMember_Remove @GroupId={groupId}, @UserId={userId}",
            cancellationToken);
        return NoContent();
    }

    [HttpPost("chat-groups/{groupId:guid}/archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveChatGroup(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ChatGroups
            .AnyAsync(group => group.GroupId == groupId, cancellationToken);
        if (!exists)
            return NotFound();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatGroup_Archive @GroupId={groupId}",
            cancellationToken);
        return NoContent();
    }

    private Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    private void AuditWorkspaceRead(Guid userId, string dataType, int skip, int take) =>
        logger.LogInformation(
            "Administrator {AdminUserId} viewed {DataType} for account {WorkspaceUserId} (skip {Skip}, take {Take}).",
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            dataType,
            userId,
            skip,
            take);

    private static bool IsValidPage(int skip, int take) =>
        skip >= 0 && take is >= 1 and <= 100;
}

public sealed record AdminDashboardResponse(
    int TotalUsers,
    int ActiveUsers,
    int InactiveUsers,
    int ActiveAdmins,
    int UsersNeedingPasswordChange,
    int TotalTasks,
    int OpenTasks,
    int CompletedTasks,
    int OverdueTasks,
    int ActiveGoals,
    int CompletedGoals,
    int TotalNotes,
    IReadOnlyList<AdminTaskStatusCountResponse> TaskStatuses,
    IReadOnlyList<AdminActivityDayResponse> ActivityByDay,
    IReadOnlyList<AdminWorkspaceSummaryResponse> Users);

public sealed record AdminTaskStatusCountResponse(string Status, int Total);

public sealed record AdminActivityDayResponse(DateTime DateUtc, int Total);

public sealed record AdminWorkspaceSummaryResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsAdmin,
    bool IsActive,
    bool MustChangePassword,
    DateTime CreatedAtUtc,
    int TotalTasks,
    int OpenTasks,
    int CompletedTasks,
    int OverdueTasks,
    int TotalGoals,
    int ActiveGoals,
    int CompletedGoals,
    int TotalNotes,
    DateTime? LastActivityAtUtc);

public sealed record AdminPageResponse<T>(
    IReadOnlyList<T> Items,
    int Skip,
    int Take,
    int TotalCount);

public sealed record AdminTaskResponse(
    Guid TaskId,
    string Title,
    string? Description,
    string Status,
    byte Priority,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool IsDeleted);

public sealed record AdminGoalResponse(
    Guid GoalId,
    string Title,
    string? Description,
    string Status,
    byte Progress,
    DateOnly? TargetDate,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool IsDeleted,
    IReadOnlyList<AdminGoalMilestoneResponse> Milestones);

public sealed record AdminGoalMilestoneResponse(string Title, bool IsCompleted, short SortOrder);

public sealed record AdminNoteResponse(
    Guid NoteId,
    string Title,
    string Content,
    string Color,
    bool IsPinned,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record AdminUserResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsAdmin,
    bool IsActive,
    bool MustChangePassword,
    DateTime CreatedAtUtc);

public sealed record CreateUserRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress, StringLength(256)] string Email);

public sealed record CreatedUserResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string TemporaryPassword,
    bool MustChangePassword);

public sealed record CreateInvitationRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress, StringLength(256)] string Email);

public sealed record InvitationCreatedResponse(string Email, DateTime ExpiresAtUtc);

public sealed record SetUserAccessRequest(bool IsAdmin, bool IsActive);

public sealed record AdminChatGroupResponse(
    Guid GroupId,
    string Name,
    DateTime CreatedAtUtc,
    bool IsArchived,
    List<Guid> MemberIds);

public sealed record CreateChatGroupRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, MinLength(1)] List<Guid> MemberIds);

public sealed record ChangeChatGroupMemberRequest(Guid UserId);
