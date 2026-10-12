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
[Route("api/activity")]
public sealed class TrashController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("trash")]
    public async Task<ActionResult<IReadOnlyList<TrashItemResponse>>> GetTrash(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var tasks = await dbContext.Tasks.AsNoTracking()
            .Where(task => task.UserId == user.Id && task.IsDeleted)
            .Select(task => new TrashItemResponse("task", task.TaskId, task.Title, task.DeletedAtUtc))
            .ToListAsync(cancellationToken);
        var goals = await dbContext.Goals.AsNoTracking()
            .Where(goal => goal.UserId == user.Id && goal.IsDeleted)
            .Select(goal => new TrashItemResponse("goal", goal.GoalId, goal.Title, goal.DeletedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(tasks.Concat(goals)
            .OrderByDescending(item => item.DeletedAtUtc)
            .ToArray());
    }

    [HttpPost("trash/{entityType}/{id:guid}/restore")]
    public async Task<IActionResult> RestoreItem(string entityType, Guid id, CancellationToken cancellationToken)
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

        if (string.Equals(entityType, "task", StringComparison.OrdinalIgnoreCase))
        {
            var task = await dbContext.Tasks
                .SingleOrDefaultAsync(item => item.TaskId == id && item.UserId == user.Id && item.IsDeleted, cancellationToken);
            if (task is null)
            {
                return NotFound();
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            task.IsDeleted = false;
            task.DeletedAtUtc = null;
            task.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddActivityAsync(user.Id, "task", task.TaskId, "restored", task.Title, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        if (string.Equals(entityType, "goal", StringComparison.OrdinalIgnoreCase))
        {
            var goal = await dbContext.Goals
                .SingleOrDefaultAsync(item => item.GoalId == id && item.UserId == user.Id && item.IsDeleted, cancellationToken);
            if (goal is null)
            {
                return NotFound();
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            goal.IsDeleted = false;
            goal.DeletedAtUtc = null;
            goal.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddActivityAsync(user.Id, "goal", goal.GoalId, "restored", goal.Title, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        return BadRequest(new { message = "Entity type must be task or goal." });
    }

    private Task<int> AddActivityAsync(
        Guid userId,
        string entityType,
        Guid entityId,
        string action,
        string title,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ActivityLog_Insert @UserId={userId}, @EntityType={entityType}, @EntityId={entityId}, @Action={action}, @Title={title}",
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
}

public sealed record TrashItemResponse(string EntityType, Guid EntityId, string Title, DateTime? DeletedAtUtc);
