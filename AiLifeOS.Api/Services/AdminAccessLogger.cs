using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Services;

public sealed class AdminAccessLogger(AiLifeOsDbContext dbContext)
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    public async Task LogAsync(Guid adminId, Guid targetId, string resource, CancellationToken ct)
    {
        if (adminId == targetId) return;

        var since = DateTime.UtcNow - DedupWindow;
        var seenRecently = await dbContext.AdminAccessLog.AsNoTracking().AnyAsync(log =>
            log.AdminUserId == adminId && log.TargetUserId == targetId
            && log.Resource == resource && log.CreatedAtUtc >= since, ct);
        if (seenRecently) return;

        var names = await dbContext.UserProfiles.AsNoTracking()
            .Where(user => user.UserId == adminId || user.UserId == targetId)
            .Select(user => new { user.UserId, user.DisplayName })
            .ToListAsync(ct);
        string NameOf(Guid id) => names.FirstOrDefault(n => n.UserId == id)?.DisplayName ?? "Unknown";

        dbContext.AdminAccessLog.Add(new AdminAccessLogEntry
        {
            AdminUserId = adminId,
            AdminDisplayName = NameOf(adminId),
            TargetUserId = targetId,
            TargetDisplayName = NameOf(targetId),
            Resource = resource,
        });
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdminAccessLogDto>> RecentAsync(int take, CancellationToken ct)
    {
        var rows = await dbContext.AdminAccessLog.AsNoTracking()
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        // Mark as UTC so the browser doesn't read these as local time
        return rows.Select(log => new AdminAccessLogDto(
            log.AdminDisplayName,
            log.TargetDisplayName,
            log.Resource,
            DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc))).ToArray();
    }
}

public sealed record AdminAccessLogDto(
    string AdminName, string TargetName, string Resource, DateTime CreatedAtUtc);