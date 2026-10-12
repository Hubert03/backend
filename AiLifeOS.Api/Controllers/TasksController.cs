using System.ComponentModel.DataAnnotations;
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
[Route("api/tasks")]
public sealed class TasksController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly string[] ValidStatuses = ["Pending", "InProgress", "Completed", "Cancelled"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskResponse>>> GetTasks(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var tasks = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.UserId == user.Id && !task.IsDeleted)
            .OrderBy(task => task.Status == "Completed")
            .ThenBy(task => task.DueAtUtc)
            .ThenBy(task => task.Priority)
            .Select(task => new TaskResponse(
                task.TaskId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueAtUtc,
                task.CreatedAtUtc,
                task.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(tasks);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> GetTask(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var task = await dbContext.Tasks
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TaskId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);
        return task is null ? NotFound() : Ok(ToResponse(task));
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> CreateTask(
        SaveTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        if (!IsValidStatus(request.Status))
        {
            return BadRequest(new { message = "Choose a valid task status." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var taskId = Guid.NewGuid();
        var title = request.Title.Trim();
        var description = NormalizeDescription(request.Description);
        var dueAtUtc = NormalizeUtc(request.DueAtUtc);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.Task_Create @TaskId={taskId}, @UserId={user.Id}, @Title={title}, @Description={description}, @Status={request.Status}, @Priority={request.Priority}, @DueAtUtc={dueAtUtc}",
            cancellationToken);
        var task = await dbContext.Tasks.AsNoTracking()
            .SingleAsync(item => item.TaskId == taskId && item.UserId == user.Id, cancellationToken);

        return CreatedAtAction(nameof(GetTask), new { id = task.TaskId }, ToResponse(task));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> UpdateTask(
        Guid id,
        SaveTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        if (!IsValidStatus(request.Status))
        {
            return BadRequest(new { message = "Choose a valid task status." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var task = await dbContext.Tasks
            .SingleOrDefaultAsync(item => item.TaskId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var wasCompleted = task.Status == "Completed";
        task.Title = request.Title.Trim();
        task.Description = NormalizeDescription(request.Description);
        task.Status = request.Status;
        task.Priority = request.Priority;
        task.DueAtUtc = NormalizeUtc(request.DueAtUtc);
        task.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(user.Id, task, "updated", cancellationToken);
        if (!wasCompleted && task.Status == "Completed")
        {
            await AddActivityAsync(user.Id, task, "completed", cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(task));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid id, CancellationToken cancellationToken)
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

        var task = await dbContext.Tasks
            .SingleOrDefaultAsync(item => item.TaskId == id && item.UserId == user.Id && !item.IsDeleted, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        task.IsDeleted = true;
        task.DeletedAtUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = task.DeletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(user.Id, task, "deleted", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private Task<int> AddActivityAsync(
        Guid userId,
        TaskItem task,
        string action,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ActivityLog_Insert @UserId={userId}, @EntityType=N'task', @EntityId={task.TaskId}, @Action={action}, @Title={task.Title}",
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

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static TaskResponse ToResponse(TaskItem task) =>
        new(
            task.TaskId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.DueAtUtc,
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
}

public sealed record SaveTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description,
    [Required, StringLength(20)] string Status,
    [Range(1, 4)] byte Priority,
    DateTime? DueAtUtc);

public sealed record TaskResponse(
    Guid TaskId,
    string Title,
    string? Description,
    string Status,
    byte Priority,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
