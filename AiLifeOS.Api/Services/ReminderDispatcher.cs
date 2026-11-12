using System.Data;
using System.Net;
using System.Text.Json;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;
using WebPushSubscription = WebPush.PushSubscription;

namespace AiLifeOS.Api.Services;

public sealed class ReminderDispatcher(
    IServiceScopeFactory scopes,
    IOptions<VapidOptions> vapid,
    ILogger<ReminderDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DeliveryLease = TimeSpan.FromMinutes(2);
    private const int BatchSize = 100;
    private const int MaximumAttempts = 8;
    private bool configurationWarningLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Reminder dispatch cycle failed.");
                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await CreateDueDeliveriesAsync(cancellationToken);

        if (!vapid.Value.IsConfigured)
        {
            if (!configurationWarningLogged)
            {
                logger.LogWarning(
                    "Reminder push delivery is paused because VAPID configuration is incomplete: {Reason}",
                    vapid.Value.ConfigurationError);
                configurationWarningLogged = true;
            }
            return;
        }

        var deliveries = await ClaimDeliveriesAsync(cancellationToken);
        if (deliveries.Count == 0) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiLifeOsDbContext>();
        var subscriptionIds = deliveries.Select(delivery => delivery.PushSubscriptionId).Distinct().ToArray();
        var subscriptions = await db.PushSubscriptions
            .Where(subscription => subscriptionIds.Contains(subscription.PushSubscriptionId))
            .ToDictionaryAsync(subscription => subscription.PushSubscriptionId, cancellationToken);
        var client = new WebPushClient();
        var vapidDetails = new VapidDetails(
            vapid.Value.Subject,
            vapid.Value.PublicKey,
            vapid.Value.PrivateKey);

        foreach (var delivery in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!subscriptions.TryGetValue(delivery.PushSubscriptionId, out var subscription))
            {
                delivery.Status = "Failed";
                delivery.LastError = "The push subscription no longer exists.";
                delivery.LeaseUntilUtc = null;
                await SaveDeliveryAsync(db, delivery, cancellationToken);
                continue;
            }

            var reminder = await db.Reminders.AsNoTracking()
                .Where(item => item.ReminderId == delivery.ReminderId)
                .Select(item => new { item.ReminderId, item.Title, item.Status })
                .FirstOrDefaultAsync(cancellationToken);
            if (reminder is null || reminder.Status != "Triggered")
            {
                delivery.Status = "Failed";
                delivery.LastError = reminder is null
                    ? "The reminder no longer exists."
                    : "The reminder is no longer active.";
                delivery.LeaseUntilUtc = null;
                await SaveDeliveryAsync(db, delivery, cancellationToken);
                continue;
            }

            var payload = JsonSerializer.Serialize(new
            {
                reminderId = reminder.ReminderId,
                title = reminder.Title,
            });

            try
            {
                await client.SendNotificationAsync(
                    new WebPushSubscription(
                        subscription.Endpoint,
                        subscription.P256dh,
                        subscription.Auth),
                    payload,
                    vapidDetails,
                    cancellationToken);

                delivery.Status = "Sent";
                delivery.SentAtUtc = DateTime.UtcNow;
                delivery.LeaseUntilUtc = null;
                delivery.LastError = null;
                await SaveDeliveryAsync(db, delivery, cancellationToken);
            }
            catch (WebPushException exception) when (
                exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.ReminderPushDeliveries
                    .Where(item => item.PushSubscriptionId == subscription.PushSubscriptionId)
                    .ExecuteDeleteAsync(cancellationToken);
                db.PushSubscriptions.Remove(subscription);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                subscriptions.Remove(subscription.PushSubscriptionId);
                logger.LogInformation(
                    "Removed expired push subscription {SubscriptionId}.",
                    subscription.PushSubscriptionId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleRetry(delivery, exception);
                await SaveDeliveryAsync(db, delivery, cancellationToken);
                logger.LogWarning(
                    exception,
                    "Push delivery {DeliveryId} for reminder {ReminderId} failed on attempt {AttemptCount}.",
                    delivery.ReminderPushDeliveryId,
                    delivery.ReminderId,
                    delivery.AttemptCount);
            }
        }
    }

    private async Task CreateDueDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiLifeOsDbContext>();
        var now = DateTime.UtcNow;
        var dueIds = await db.Reminders.AsNoTracking()
            .Where(reminder => reminder.Status == "Scheduled" && reminder.DueAtUtc <= now)
            .OrderBy(reminder => reminder.DueAtUtc)
            .Select(reminder => reminder.ReminderId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var reminderId in dueIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await db.Reminders
                .Where(reminder =>
                    reminder.ReminderId == reminderId &&
                    reminder.Status == "Scheduled" &&
                    reminder.DueAtUtc <= now)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(reminder => reminder.Status, "Triggered")
                    .SetProperty(reminder => reminder.TriggeredAtUtc, now), cancellationToken);

            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var reminder = await db.Reminders.AsNoTracking()
                .Where(item => item.ReminderId == reminderId)
                .Select(item => new { item.ReminderId, item.UserId, item.DueAtUtc })
                .SingleAsync(cancellationToken);
            var subscriptionIds = await db.PushSubscriptions.AsNoTracking()
                .Where(subscription => subscription.UserId == reminder.UserId)
                .Select(subscription => subscription.PushSubscriptionId)
                .ToListAsync(cancellationToken);

            db.ReminderPushDeliveries.AddRange(subscriptionIds.Select(subscriptionId =>
                new ReminderPushDelivery
                {
                    ReminderId = reminder.ReminderId,
                    PushSubscriptionId = subscriptionId,
                    DueAtUtc = DateTime.SpecifyKind(reminder.DueAtUtc, DateTimeKind.Utc),
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<List<ReminderPushDelivery>> ClaimDeliveriesAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiLifeOsDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = DateTime.UtcNow;
        var deliveries = await db.ReminderPushDeliveries
            .Where(delivery =>
                delivery.Status == "Pending" && delivery.NextAttemptAtUtc <= now ||
                delivery.Status == "Sending" && delivery.LeaseUntilUtc <= now)
            .OrderBy(delivery => delivery.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var delivery in deliveries)
        {
            if (delivery.AttemptCount >= MaximumAttempts)
            {
                delivery.Status = "Failed";
                delivery.LeaseUntilUtc = null;
                delivery.LastError ??= "Maximum delivery attempts reached.";
                continue;
            }

            delivery.Status = "Sending";
            delivery.AttemptCount++;
            delivery.LastAttemptAtUtc = now;
            delivery.LeaseUntilUtc = now.Add(DeliveryLease);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deliveries.Where(delivery => delivery.Status == "Sending").ToList();
    }

    private static void ScheduleRetry(ReminderPushDelivery delivery, Exception exception)
    {
        delivery.LastError = exception.Message.Length <= 1000
            ? exception.Message
            : exception.Message[..1000];
        delivery.LeaseUntilUtc = null;

        if (delivery.AttemptCount >= MaximumAttempts)
        {
            delivery.Status = "Failed";
            return;
        }

        var delayMinutes = Math.Min(360, Math.Pow(2, delivery.AttemptCount - 1));
        delivery.Status = "Pending";
        delivery.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    private async Task SaveDeliveryAsync(
        AiLifeOsDbContext db,
        ReminderPushDelivery delivery,
        CancellationToken cancellationToken)
    {
        var updated = await db.ReminderPushDeliveries
            .Where(item =>
                item.ReminderPushDeliveryId == delivery.ReminderPushDeliveryId &&
                item.Status == "Sending" &&
                item.AttemptCount == delivery.AttemptCount)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, delivery.Status)
                .SetProperty(item => item.NextAttemptAtUtc, delivery.NextAttemptAtUtc)
                .SetProperty(item => item.LastAttemptAtUtc, delivery.LastAttemptAtUtc)
                .SetProperty(item => item.LeaseUntilUtc, delivery.LeaseUntilUtc)
                .SetProperty(item => item.SentAtUtc, delivery.SentAtUtc)
                .SetProperty(item => item.LastError, delivery.LastError), cancellationToken);

        if (updated == 0)
            logger.LogDebug(
                "Skipped stale update for reminder delivery {DeliveryId}.",
                delivery.ReminderPushDeliveryId);
    }
}
