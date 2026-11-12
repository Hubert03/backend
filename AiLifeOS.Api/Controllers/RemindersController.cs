using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AiLifeOS.Api.Services;
using Microsoft.Extensions.Options;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reminders")]
public sealed class RemindersController(
    AiLifeOsDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetReminders(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        var reminders = await dbContext.Reminders.AsNoTracking()
            .Where(reminder => reminder.UserId == userId)
            .OrderBy(reminder => reminder.DueAtUtc)
            .Select(reminder => new ReminderResponse(
                reminder.ReminderId,
                reminder.TaskId,
                reminder.Title,
                DateTime.SpecifyKind(reminder.DueAtUtc, DateTimeKind.Utc),
                reminder.Status,
                reminder.TriggeredAtUtc == null
                    ? null
                    : DateTime.SpecifyKind(reminder.TriggeredAtUtc.Value, DateTimeKind.Utc)))
            .ToListAsync(cancellationToken);
        return Ok(reminders);
    }

    [HttpGet("push-config")]
    public IActionResult GetPushConfiguration([FromServices] IOptions<VapidOptions> vapid) =>
        Ok(new
        {
            publicKey = vapid.Value.IsConfigured ? vapid.Value.PublicKey : null,
            enabled = vapid.Value.IsConfigured,
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReminder(
        SaveReminderRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        var validation = await ValidateReminderAsync(request, userId, cancellationToken);
        if (validation is not null) return validation;
        var reminder = new Reminder
        {
            UserId = userId,
            TaskId = request.TaskId,
            Title = request.Title.Trim(),
            DueAtUtc = DateTime.SpecifyKind(request.DueAtUtc, DateTimeKind.Utc),
        };
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(reminder));
    }

    [HttpPut("{reminderId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateReminder(
        Guid reminderId,
        SaveReminderRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        var reminder = await dbContext.Reminders.FirstOrDefaultAsync(
            item => item.ReminderId == reminderId && item.UserId == userId,
            cancellationToken);
        if (reminder is null) return NotFound();
        if (reminder.Status != "Scheduled")
            return Conflict(new { message = "Only scheduled reminders can be edited." });
        var validation = await ValidateReminderAsync(request, userId, cancellationToken);
        if (validation is not null) return validation;
        reminder.Title = request.Title.Trim();
        reminder.TaskId = request.TaskId;
        reminder.DueAtUtc = DateTime.SpecifyKind(request.DueAtUtc, DateTimeKind.Utc);
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(reminder));
    }

    [HttpPost("{reminderId:guid}/snooze")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Snooze(
        Guid reminderId,
        SnoozeReminderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Minutes is < 1 or > 120)
            return BadRequest(new { message = "Choose a snooze timer from 1 to 120 minutes." });
        var reminder = await dbContext.Reminders.FirstOrDefaultAsync(
            item => item.ReminderId == reminderId && item.UserId == CurrentUserId(),
            cancellationToken);
        if (reminder is null) return NotFound();
        if (reminder.Status != "Triggered")
            return Conflict(new { message = "Only triggered reminders can be snoozed." });
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        reminder.Status = "Scheduled";
        reminder.DueAtUtc = DateTime.UtcNow.AddMinutes(request.Minutes);
        reminder.TriggeredAtUtc = null;
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await CancelPendingDeliveriesAsync(reminderId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(reminder));
    }

    [HttpPost("{reminderId:guid}/dismiss")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(Guid reminderId, CancellationToken cancellationToken)
    {
        var reminder = await dbContext.Reminders.FirstOrDefaultAsync(
            item => item.ReminderId == reminderId && item.UserId == CurrentUserId(),
            cancellationToken);
        if (reminder is null) return NotFound();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        reminder.Status = "Dismissed";
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await CancelPendingDeliveriesAsync(reminderId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{reminderId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReminder(Guid reminderId, CancellationToken cancellationToken)
    {
        var reminder = await dbContext.Reminders.FirstOrDefaultAsync(
            item => item.ReminderId == reminderId && item.UserId == CurrentUserId(),
            cancellationToken);
        if (reminder is null) return NotFound();
        dbContext.Reminders.Remove(reminder);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("push-subscription")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePushSubscription(
        SavePushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            return BadRequest(new { message = "A valid HTTPS push endpoint is required." });
        if (request.P256dh.Length is < 1 or > 256 || request.Auth.Length is < 1 or > 256)
            return BadRequest(new { message = "The push subscription keys are invalid." });

        var userId = CurrentUserId();
        var subscription = await dbContext.PushSubscriptions.FirstOrDefaultAsync(
            item => item.Endpoint == request.Endpoint,
            cancellationToken);
        if (subscription is not null && subscription.UserId != userId)
            return Conflict(new { message = "This push subscription is already registered to another account." });
        if (subscription is null)
        {
            subscription = new PushSubscription { UserId = userId, Endpoint = request.Endpoint };
            dbContext.PushSubscriptions.Add(subscription);
        }
        subscription.P256dh = request.P256dh;
        subscription.Auth = request.Auth;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("push-subscription")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePushSubscription(
        RemovePushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.PushSubscriptions.FirstOrDefaultAsync(
            item => item.UserId == CurrentUserId() && item.Endpoint == request.Endpoint,
            cancellationToken);
        if (subscription is not null)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.ReminderPushDeliveries
                .Where(delivery => delivery.PushSubscriptionId == subscription.PushSubscriptionId)
                .ExecuteDeleteAsync(cancellationToken);
            dbContext.PushSubscriptions.Remove(subscription);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return NoContent();
    }

    private async Task<IActionResult?> ValidateReminderAsync(
        SaveReminderRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return BadRequest(new { message = "Reminder titles must contain 1 to 200 characters." });
        if (request.DueAtUtc.Kind == DateTimeKind.Local ||
            request.DueAtUtc <= DateTime.UtcNow)
            return BadRequest(new { message = "Choose a future UTC date and time." });
        if (request.TaskId is Guid taskId &&
            !await dbContext.Tasks.AnyAsync(
                task => task.TaskId == taskId && task.UserId == userId && !task.IsDeleted,
                cancellationToken))
            return BadRequest(new { message = "Choose an active task that belongs to your account." });
        return null;
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Task<int> CancelPendingDeliveriesAsync(
        Guid reminderId,
        CancellationToken cancellationToken) =>
        dbContext.ReminderPushDeliveries
            .Where(delivery =>
                delivery.ReminderId == reminderId &&
                (delivery.Status == "Pending" || delivery.Status == "Sending"))
            .ExecuteUpdateAsync(update => update
                .SetProperty(delivery => delivery.Status, "Failed")
                .SetProperty(delivery => delivery.LastError, "Reminder was snoozed or dismissed.")
                .SetProperty(delivery => delivery.LeaseUntilUtc, (DateTime?)null),
                cancellationToken);

    private static ReminderResponse ToResponse(Reminder reminder) =>
        new(reminder.ReminderId, reminder.TaskId, reminder.Title,
            DateTime.SpecifyKind(reminder.DueAtUtc, DateTimeKind.Utc), reminder.Status,
            reminder.TriggeredAtUtc is null
                ? null
                : DateTime.SpecifyKind(reminder.TriggeredAtUtc.Value, DateTimeKind.Utc));
}

public sealed record SaveReminderRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    DateTime DueAtUtc,
    Guid? TaskId);

public sealed record SnoozeReminderRequest(int Minutes);

public sealed record SavePushSubscriptionRequest(
    [Required, StringLength(2048)] string Endpoint,
    [Required, StringLength(256)] string P256dh,
    [Required, StringLength(256)] string Auth);

public sealed record RemovePushSubscriptionRequest(
    [Required, StringLength(2048)] string Endpoint);

public sealed record ReminderResponse(
    Guid ReminderId,
    Guid? TaskId,
    string Title,
    DateTime DueAtUtc,
    string Status,
    DateTime? TriggeredAtUtc);
