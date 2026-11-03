using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/team/workspace")]
public sealed class TeamWorkspaceController(
    AiLifeOsDbContext dbContext,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetWorkspace(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var members = await userManager.Users.AsNoTracking()
            .Where(user => user.TeamId == context.TeamId && user.IsActive)
            .OrderBy(user => user.DisplayName)
            .Select(user => new WorkspaceMemberResponse(user.Id, user.DisplayName, user.AccessRole))
            .ToListAsync(cancellationToken);
        return Ok(new WorkspaceResponse(
            context.TeamId,
            context.TeamName,
            context.AccessRole,
            context.CanManageAll,
            context.UserId,
            members));
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var rows = await dbContext.TeamTasks.AsNoTracking()
            .Where(task => task.TeamId == context.TeamId && !task.IsDeleted)
            .OrderBy(task => task.DueAtUtc)
            .ThenByDescending(task => task.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var userIds = rows.Select(task => task.CreatedByUserId)
            .Concat(rows.Where(task => task.AssignedToUserId.HasValue)
                .Select(task => task.AssignedToUserId!.Value))
            .Distinct()
            .ToArray();
        var users = await userManager.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return Ok(rows.Select(task => new TeamTaskResponse(
            task.TaskId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            Utc(task.DueAtUtc),
            task.AssignedToUserId,
            task.CreatedByUserId,
            users.GetValueOrDefault(task.CreatedByUserId, "Unknown"),
            task.AssignedToUserId is Guid assignedId ? users.GetValueOrDefault(assignedId) : null,
            Utc(task.CreatedAtUtc),
            Utc(task.UpdatedAtUtc))));
    }

    [HttpPost("tasks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTask(
        SaveTeamTaskRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var validation = await ValidateTaskAsync(request, context, cancellationToken);
        if (validation is not null) return validation;
        var task = new TeamTask
        {
            TeamId = context.TeamId,
            CreatedByUserId = context.UserId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Status = request.Status,
            Priority = (byte)request.Priority,
            DueAtUtc = Utc(request.DueAtUtc),
            AssignedToUserId = request.AssignedToUserId,
        };
        dbContext.TeamTasks.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ToTaskResponseAsync(task));
    }

    [HttpPut("tasks/{taskId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTask(
        Guid taskId,
        SaveTeamTaskRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var task = await dbContext.TeamTasks.FirstOrDefaultAsync(
            item => item.TaskId == taskId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (task is null) return NotFound();
        if (!CanEdit(context, task.CreatedByUserId)) return Forbid();
        var validation = await ValidateTaskAsync(request, context, cancellationToken);
        if (validation is not null) return validation;
        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.Status = request.Status;
        task.Priority = (byte)request.Priority;
        task.DueAtUtc = Utc(request.DueAtUtc);
        task.AssignedToUserId = request.AssignedToUserId;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ToTaskResponseAsync(task));
    }

    [HttpDelete("tasks/{taskId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var task = await dbContext.TeamTasks.FirstOrDefaultAsync(
            item => item.TaskId == taskId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (task is null) return NotFound();
        if (!CanEdit(context, task.CreatedByUserId)) return Forbid();
        task.IsDeleted = true;
        task.DeletedAtUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = task.DeletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("goals")]
    public async Task<IActionResult> GetGoals(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var goals = await dbContext.TeamGoals.AsNoTracking()
            .Include(goal => goal.Milestones)
            .Where(goal => goal.TeamId == context.TeamId && !goal.IsDeleted)
            .OrderBy(goal => goal.TargetDate)
            .ThenByDescending(goal => goal.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var creatorIds = goals.Select(goal => goal.CreatedByUserId).Distinct().ToArray();
        var names = await userManager.Users.AsNoTracking()
            .Where(user => creatorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return Ok(goals.Select(goal => ToGoalResponse(goal, names.GetValueOrDefault(goal.CreatedByUserId, "Unknown"))));
    }

    [HttpPost("goals")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGoal(
        SaveTeamGoalRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var validation = ValidateGoal(request);
        if (validation is not null) return validation;
        var goal = new TeamGoal
        {
            TeamId = context.TeamId,
            CreatedByUserId = context.UserId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Status = request.Status,
            Progress = (byte)request.Progress,
            TargetDate = request.TargetDate?.Date,
            Milestones = request.Milestones.Select((item, index) => new TeamGoalMilestone
            {
                Title = item.Title.Trim(),
                IsCompleted = item.IsCompleted,
                SortOrder = (short)index,
            }).ToList(),
        };
        dbContext.TeamGoals.Add(goal);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToGoalResponse(goal, context.DisplayName));
    }

    [HttpPut("goals/{goalId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGoal(
        Guid goalId,
        SaveTeamGoalRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var goal = await dbContext.TeamGoals.Include(item => item.Milestones).FirstOrDefaultAsync(
            item => item.GoalId == goalId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (goal is null) return NotFound();
        if (!CanEdit(context, goal.CreatedByUserId)) return Forbid();
        var validation = ValidateGoal(request);
        if (validation is not null) return validation;
        goal.Title = request.Title.Trim();
        goal.Description = request.Description?.Trim();
        goal.Status = request.Status;
        goal.Progress = (byte)request.Progress;
        goal.TargetDate = request.TargetDate?.Date;
        goal.UpdatedAtUtc = DateTime.UtcNow;
        dbContext.TeamGoalMilestones.RemoveRange(goal.Milestones);
        goal.Milestones = request.Milestones.Select((item, index) => new TeamGoalMilestone
        {
            GoalId = goalId,
            Title = item.Title.Trim(),
            IsCompleted = item.IsCompleted,
            SortOrder = (short)index,
        }).ToList();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToGoalResponse(goal, context.DisplayName));
    }

    [HttpDelete("goals/{goalId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGoal(Guid goalId, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var goal = await dbContext.TeamGoals.FirstOrDefaultAsync(
            item => item.GoalId == goalId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (goal is null) return NotFound();
        if (!CanEdit(context, goal.CreatedByUserId)) return Forbid();
        goal.IsDeleted = true;
        goal.DeletedAtUtc = DateTime.UtcNow;
        goal.UpdatedAtUtc = goal.DeletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("notes")]
    public async Task<IActionResult> GetNotes(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var notes = await dbContext.TeamNotes.AsNoTracking()
            .Where(note => note.TeamId == context.TeamId && !note.IsDeleted)
            .OrderByDescending(note => note.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var creatorIds = notes.Select(note => note.CreatedByUserId).Distinct().ToArray();
        var names = await userManager.Users.AsNoTracking()
            .Where(user => creatorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return Ok(notes.Select(note => ToNoteResponse(note, names.GetValueOrDefault(note.CreatedByUserId, "Unknown"))));
    }

    [HttpPost("notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateNote(
        SaveTeamNoteRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var validation = ValidateNote(request);
        if (validation is not null) return validation;
        var note = new TeamNote
        {
            TeamId = context.TeamId,
            CreatedByUserId = context.UserId,
            Title = request.Title.Trim(),
            Content = request.Content,
            Color = request.Color.ToUpperInvariant(),
        };
        dbContext.TeamNotes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToNoteResponse(note, context.DisplayName));
    }

    [HttpPut("notes/{noteId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNote(
        Guid noteId,
        SaveTeamNoteRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var note = await dbContext.TeamNotes.FirstOrDefaultAsync(
            item => item.NoteId == noteId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (note is null) return NotFound();
        if (!CanEdit(context, note.CreatedByUserId)) return Forbid();
        var validation = ValidateNote(request);
        if (validation is not null) return validation;
        note.Title = request.Title.Trim();
        note.Content = request.Content;
        note.Color = request.Color.ToUpperInvariant();
        note.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToNoteResponse(note, context.DisplayName));
    }

    [HttpDelete("notes/{noteId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNote(Guid noteId, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context is null) return NotFound();
        var note = await dbContext.TeamNotes.FirstOrDefaultAsync(
            item => item.NoteId == noteId && item.TeamId == context.TeamId && !item.IsDeleted,
            cancellationToken);
        if (note is null) return NotFound();
        if (!CanEdit(context, note.CreatedByUserId)) return Forbid();
        note.IsDeleted = true;
        note.DeletedAtUtc = DateTime.UtcNow;
        note.UpdatedAtUtc = note.DeletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<WorkspaceContext?> GetContextAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;
        var user = await userManager.Users.AsNoTracking()
            .Where(item => item.Id == userId && item.IsActive && item.TeamId != null)
            .Select(item => new { item.Id, item.DisplayName, item.AccessRole, TeamId = item.TeamId!.Value })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null) return null;
        var team = await dbContext.Teams.AsNoTracking()
            .Where(item => item.TeamId == user.TeamId)
            .Select(item => new { item.TeamId, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (team is null) return null;
        return new WorkspaceContext(
            user.Id, user.DisplayName, user.AccessRole, team.TeamId, team.Name,
            user.AccessRole is "Manager" or "TeamLead");
    }

    private async Task<IActionResult?> ValidateTaskAsync(
        SaveTeamTaskRequest request,
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return BadRequest(new { message = "Task titles must contain 1 to 200 characters." });
        if (request.Description?.Length > 2000)
            return BadRequest(new { message = "Task descriptions cannot exceed 2000 characters." });
        if (request.Status is not ("Pending" or "InProgress" or "Completed" or "Cancelled"))
            return BadRequest(new { message = "Choose a valid task status." });
        if (request.Priority is < 1 or > 4)
            return BadRequest(new { message = "Task priority must be between 1 and 4." });
        if (request.AssignedToUserId is Guid assignedId &&
            !await userManager.Users.AnyAsync(
                user => user.Id == assignedId && user.TeamId == context.TeamId && user.IsActive,
                cancellationToken))
            return BadRequest(new { message = "Assign the task to an active member of this team." });
        return null;
    }

    private static IActionResult? ValidateGoal(SaveTeamGoalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return new BadRequestObjectResult(new { message = "Goal titles must contain 1 to 200 characters." });
        if (request.Description?.Length > 2000)
            return new BadRequestObjectResult(new { message = "Goal descriptions cannot exceed 2000 characters." });
        if (request.Status is not ("Active" or "Paused" or "Completed"))
            return new BadRequestObjectResult(new { message = "Choose a valid goal status." });
        if (request.Progress is < 0 or > 100)
            return new BadRequestObjectResult(new { message = "Goal progress must be between 0 and 100." });
        if (request.Milestones.Count > 100 ||
            request.Milestones.Any(item => string.IsNullOrWhiteSpace(item.Title) || item.Title.Trim().Length > 200))
            return new BadRequestObjectResult(new { message = "Milestones must have titles of 1 to 200 characters, with no more than 100 per goal." });
        return null;
    }

    private static IActionResult? ValidateNote(SaveTeamNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return new BadRequestObjectResult(new { message = "Note titles must contain 1 to 200 characters." });
        if (request.Content.Length > 100000)
            return new BadRequestObjectResult(new { message = "Note content is too long." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.Color, "^#[0-9A-Fa-f]{6}$"))
            return new BadRequestObjectResult(new { message = "Choose a valid six-digit hex color." });
        return null;
    }

    private async Task<TeamTaskResponse> ToTaskResponseAsync(TeamTask task)
    {
        var creator = await userManager.FindByIdAsync(task.CreatedByUserId.ToString());
        var assignee = task.AssignedToUserId is Guid assigneeId
            ? await userManager.FindByIdAsync(assigneeId.ToString())
            : null;
        return new TeamTaskResponse(
            task.TaskId, task.Title, task.Description, task.Status, task.Priority,
            Utc(task.DueAtUtc), task.AssignedToUserId, task.CreatedByUserId,
            creator?.DisplayName ?? "Unknown", assignee?.DisplayName,
            Utc(task.CreatedAtUtc), Utc(task.UpdatedAtUtc));
    }

    private static TeamGoalResponse ToGoalResponse(TeamGoal goal, string creatorName) =>
        new(goal.GoalId, goal.Title, goal.Description, goal.Status, goal.Progress, goal.TargetDate?.ToString("yyyy-MM-dd"),
            goal.CreatedByUserId, creatorName, Utc(goal.CreatedAtUtc), Utc(goal.UpdatedAtUtc),
            goal.Milestones.OrderBy(item => item.SortOrder)
                .Select(item => new TeamGoalMilestoneResponse(item.Title, item.IsCompleted, item.SortOrder))
                .ToArray());

    private static TeamNoteResponse ToNoteResponse(TeamNote note, string creatorName) =>
        new(note.NoteId, note.Title, note.Content, note.Color, note.CreatedByUserId, creatorName,
            Utc(note.CreatedAtUtc), Utc(note.UpdatedAtUtc));

    private bool CanEdit(WorkspaceContext context, Guid creatorId) =>
        context.CanManageAll || context.UserId == creatorId;

    private static DateTime Utc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : Utc(value.Value);

    private sealed record WorkspaceContext(
        Guid UserId, string DisplayName, string AccessRole, Guid TeamId, string TeamName, bool CanManageAll);
}

public sealed record WorkspaceResponse(
    Guid TeamId, string TeamName, string AccessRole, bool CanManageAll, Guid CurrentUserId,
    IReadOnlyList<WorkspaceMemberResponse> Members);

public sealed record WorkspaceMemberResponse(Guid UserId, string DisplayName, string AccessRole);

public sealed record SaveTeamTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    string Status,
    int Priority,
    DateTime? DueAtUtc,
    Guid? AssignedToUserId);

public sealed record TeamTaskResponse(
    Guid TaskId, string Title, string? Description, string Status, byte Priority, DateTime? DueAtUtc,
    Guid? AssignedToUserId, Guid CreatedByUserId, string CreatedByDisplayName, string? AssignedToDisplayName,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public sealed record SaveTeamGoalRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    string Status,
    int Progress,
    DateTime? TargetDate,
    IReadOnlyList<SaveTeamGoalMilestoneRequest> Milestones);

public sealed record SaveTeamGoalMilestoneRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    bool IsCompleted);

public sealed record TeamGoalResponse(
    Guid GoalId, string Title, string? Description, string Status, byte Progress, string? TargetDate,
    Guid CreatedByUserId, string CreatedByDisplayName, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc,
    IReadOnlyList<TeamGoalMilestoneResponse> Milestones);

public sealed record TeamGoalMilestoneResponse(string Title, bool IsCompleted, short SortOrder);

public sealed record SaveTeamNoteRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required] string Content,
    [Required] string Color);

public sealed record TeamNoteResponse(
    Guid NoteId, string Title, string Content, string Color, Guid CreatedByUserId,
    string CreatedByDisplayName, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
