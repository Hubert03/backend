using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/goals")]
public sealed class GoalsController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly string[] ValidStatuses = ["Active", "Paused", "Completed"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GoalResponse>>> GetGoals(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var goals = await dbContext.Goals
            .AsNoTracking()
            .Where(goal => goal.UserId == user.Id && !goal.IsDeleted)
            .Include(goal => goal.Milestones)
            .OrderBy(goal => goal.Status == "Completed")
            .ThenBy(goal => goal.TargetDate)
            .ThenBy(goal => goal.Title)
            .ToListAsync(cancellationToken);

        return Ok(goals.Select(ToResponse).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoalResponse>> GetGoal(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var goal = await dbContext.Goals
            .AsNoTracking()
            .Include(item => item.Milestones)
            .SingleOrDefaultAsync(item => item.GoalId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);

        return goal is null ? NotFound() : Ok(ToResponse(goal));
    }

    [HttpPost]
    public async Task<ActionResult<GoalResponse>> CreateGoal(
        SaveGoalRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }
        if (!IsValidStatus(request.Status))
        {
            return BadRequest(new { message = "Choose a valid goal status." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var goalId = Guid.NewGuid();
        var milestones = request.Milestones
            .Where(milestone => !string.IsNullOrWhiteSpace(milestone.Title))
            .Select((milestone, index) => new
            {
                title = milestone.Title.Trim(),
                isCompleted = milestone.IsCompleted,
                sortOrder = (short)index
            })
            .ToArray();
        var milestonesJson = JsonSerializer.Serialize(milestones);
        var title = request.Title.Trim();
        var description = NormalizeDescription(request.Description);
        var targetDate = request.TargetDate?.ToDateTime(TimeOnly.MinValue);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.Goal_Create @GoalId={goalId}, @UserId={user.Id}, @Title={title}, @Description={description}, @Status={request.Status}, @Progress={request.Progress}, @TargetDate={targetDate}, @MilestonesJson={milestonesJson}",
            cancellationToken);
        var goal = await dbContext.Goals.AsNoTracking()
            .Include(item => item.Milestones)
            .SingleAsync(item => item.GoalId == goalId && item.UserId == user.Id, cancellationToken);

        return CreatedAtAction(nameof(GetGoal), new { id = goal.GoalId }, ToResponse(goal));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GoalResponse>> UpdateGoal(
        Guid id,
        SaveGoalRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }
        if (!IsValidStatus(request.Status))
        {
            return BadRequest(new { message = "Choose a valid goal status." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var goal = await dbContext.Goals
            .Include(item => item.Milestones)
            .SingleOrDefaultAsync(item => item.GoalId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);
        if (goal is null)
        {
            return NotFound();
        }

        var milestones = request.Milestones
            .Where(milestone => !string.IsNullOrWhiteSpace(milestone.Title))
            .Select((milestone, index) => new
            {
                title = milestone.Title.Trim(),
                isCompleted = milestone.IsCompleted,
                sortOrder = (short)index
            })
            .ToArray();
        var milestonesJson = JsonSerializer.Serialize(milestones);
        var title = request.Title.Trim();
        var description = NormalizeDescription(request.Description);
        var targetDate = request.TargetDate?.ToDateTime(TimeOnly.MinValue);
        var updatedAtUtc = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.Goal_Update @GoalId={goal.GoalId}, @UserId={user.Id}, @Title={title}, @Description={description}, @Status={request.Status}, @Progress={request.Progress}, @TargetDate={targetDate}, @UpdatedAtUtc={updatedAtUtc}, @MilestonesJson={milestonesJson}",
            cancellationToken);
        var updatedGoal = await dbContext.Goals.AsNoTracking()
            .Include(item => item.Milestones)
            .SingleAsync(item => item.GoalId == goal.GoalId && item.UserId == user.Id, cancellationToken);
        return Ok(ToResponse(updatedGoal));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGoal(Guid id, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var goal = await dbContext.Goals
            .SingleOrDefaultAsync(item => item.GoalId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);
        if (goal is null)
        {
            return NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        goal.IsDeleted = true;
        goal.DeletedAtUtc = DateTime.UtcNow;
        goal.UpdatedAtUtc = goal.DeletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(user.Id, goal, "deleted", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private Task<int> AddActivityAsync(
        Guid userId,
        GoalItem goal,
        string action,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ActivityLog_Insert @UserId={userId}, @EntityType=N'goal', @EntityId={goal.GoalId}, @Action={action}, @Title={goal.Title}",
            cancellationToken);

    private async Task<bool> HasValidCsrfTokenAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool IsValidStatus(string status) =>
        ValidStatuses.Contains(status, StringComparer.Ordinal);

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static GoalResponse ToResponse(GoalItem goal) =>
        new(
            goal.GoalId,
            goal.Title,
            goal.Description,
            goal.Status,
            goal.Progress,
            goal.TargetDate,
            goal.CreatedAtUtc,
            goal.UpdatedAtUtc,
            goal.Milestones
                .OrderBy(milestone => milestone.SortOrder)
                .Select(milestone => new GoalMilestoneResponse(
                    milestone.Title,
                    milestone.IsCompleted,
                    milestone.SortOrder))
                .ToArray());
}

public sealed record SaveGoalRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    [Required, StringLength(20)] string Status,
    [Range(0, 100)] byte Progress,
    DateOnly? TargetDate,
    [Required, MaxLength(30)] IReadOnlyList<SaveGoalMilestoneRequest> Milestones);

public sealed record SaveGoalMilestoneRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    bool IsCompleted);

public sealed record GoalResponse(
    Guid GoalId,
    string Title,
    string? Description,
    string Status,
    byte Progress,
    DateOnly? TargetDate,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<GoalMilestoneResponse> Milestones);

public sealed record GoalMilestoneResponse(string Title, bool IsCompleted, short SortOrder);
