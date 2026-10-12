using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/activity")]
public sealed class ActivityController(
    AiLifeOsDbContext dbContext,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> GetActivity(
        [FromQuery] int take = 12,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100)
        {
            return BadRequest(new { message = "Take must be between 1 and 100." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var activities = await dbContext.ActivityLog
            .AsNoTracking()
            .Where(activity => activity.UserId == user.Id)
            .OrderByDescending(activity => activity.CreatedAtUtc)
            .Take(take)
            .Select(activity => new ActivityResponse(
                activity.ActivityLogId,
                activity.Action,
                activity.EntityType,
                activity.EntityId,
                activity.Title,
                activity.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(activities);
    }
}

public sealed record ActivityResponse(
    Guid ActivityId,
    string Action,
    string EntityType,
    Guid EntityId,
    string Title,
    DateTime CreatedAtUtc);
