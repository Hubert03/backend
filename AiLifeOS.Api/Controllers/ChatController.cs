using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public sealed class ChatController(
    AiLifeOsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IHubContext<ChatHub> chatHub) : ControllerBase
{
[HttpGet("groups")]
public async Task<IActionResult> GetGroups(CancellationToken cancellationToken)
{
    var userId = GetUserId();

    var groups = await dbContext.ChatGroupMembers
        .AsNoTracking()
        .Where(member => member.UserId == userId)
        .Join(
            dbContext.ChatGroups
                .AsNoTracking()
                .Where(group => !group.IsArchived),
            member => member.GroupId,
            group => group.GroupId,
            (member, group) => group
        )
        .OrderBy(group => group.Name)
        .Select(group => new ChatGroupResponse(
            group.GroupId,
            group.Name
        ))
        .ToListAsync(cancellationToken);

    return Ok(groups);
}

    [HttpGet("groups/{groupId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(groupId, userId, cancellationToken))
            return NotFound();

        var messages = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.GroupId == groupId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(100)
            .OrderBy(message => message.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(await ToResponsesAsync(messages, cancellationToken));
    }

    [HttpGet("groups/{groupId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid groupId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(groupId, userId, cancellationToken))
            return NotFound();
        var members = await dbContext.ChatGroupMembers.AsNoTracking()
            .Where(member => member.GroupId == groupId)
            .Join(
                userManager.Users.AsNoTracking().Where(user => user.IsActive),
                member => member.UserId,
                user => user.Id,
                (_, user) => user)
            .OrderBy(user => user.DisplayName)
            .Select(user => new ChatGroupMemberResponse(user.Id, user.DisplayName))
            .ToListAsync(cancellationToken);
        return Ok(members);
    }

    [HttpPost("groups/{groupId:guid}/messages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage(
        Guid groupId,
        SendChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        if (content.Length == 0)
            return BadRequest(new { message = "Message cannot be empty." });

        var userId = GetUserId();
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
            return Unauthorized();
        if (!await IsMemberAsync(groupId, userId, cancellationToken))
            return NotFound();

        var messageId = Guid.NewGuid();
        var mentionedUserIds = request.MentionedUserIds ?? [];
        var mentionedUsers = await dbContext.ChatGroupMembers
            .Where(member => member.GroupId == groupId
                && mentionedUserIds.Contains(member.UserId))
            .Join(
                userManager.Users.Where(member => member.IsActive),
                membership => membership.UserId,
                member => member.Id,
                (_, member) => new { member.Id, member.DisplayName })
            .ToListAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatMessage_Create @MessageId={messageId}, @GroupId={groupId}, @UserId={userId}, @DisplayName={user.DisplayName}, @Content={content}, @ReplyToMessageId={request.ReplyToMessageId}",
            cancellationToken);
        dbContext.ChatMessageMentions.AddRange(mentionedUsers.Select(mentionedUser =>
            new ChatMessageMention
            {
                MessageId = messageId,
                MentionedUserId = mentionedUser.Id,
                DisplayName = mentionedUser.DisplayName,
            }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = (await ToResponsesAsync(
            [await dbContext.ChatMessages.AsNoTracking().SingleAsync(
                item => item.MessageId == messageId, cancellationToken)],
            cancellationToken))[0];

        var memberIds = await dbContext.ChatGroupMembers
            .Where(member => member.GroupId == groupId)
            .Join(
                userManager.Users.Where(member => member.IsActive),
                membership => membership.UserId,
                member => member.Id,
                (_, member) => member.Id.ToString())
            .ToListAsync(cancellationToken);

        await chatHub.Clients.Users(memberIds)
            .SendAsync("MessageReceived", message, cancellationToken);

        return Ok(message);
    }

    [HttpDelete("groups/{groupId:guid}/messages/{messageId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMessage(
        Guid groupId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(groupId, userId, cancellationToken))
            return NotFound();
        var message = await dbContext.ChatMessages.FirstOrDefaultAsync(
            item => item.MessageId == messageId && item.GroupId == groupId,
            cancellationToken);
        if (message is null) return NotFound();
        if (message.IsDeleted) return NoContent();

        var currentUser = await userManager.FindByIdAsync(userId.ToString());
        var author = await userManager.FindByIdAsync(message.UserId.ToString());
        var canDelete = currentUser is not null &&
            (message.UserId == userId ||
             currentUser.IsAdmin ||
             currentUser.AccessRole == "Manager" &&
             currentUser.TeamId is not null &&
             currentUser.TeamId == author?.TeamId);
        if (!canDelete) return Forbid();

        message.IsDeleted = true;
        message.DeletedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
        var deleted = (await ToResponsesAsync([message], cancellationToken))[0];
        var memberIds = await dbContext.ChatGroupMembers
            .Where(member => member.GroupId == groupId)
            .Select(member => member.UserId.ToString())
            .ToListAsync(cancellationToken);
        await chatHub.Clients.Users(memberIds)
            .SendAsync("MessageDeleted", deleted, cancellationToken);
        return NoContent();
    }

    private async Task<IReadOnlyList<ChatMessageResponse>> ToResponsesAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0) return [];
        var ids = messages.Select(message => message.MessageId).ToArray();
        var replyIds = messages.Where(message => message.ReplyToMessageId.HasValue)
            .Select(message => message.ReplyToMessageId!.Value)
            .Distinct()
            .ToArray();
        var relatedMessages = await dbContext.ChatMessages.AsNoTracking()
            .Where(message => replyIds.Contains(message.MessageId))
            .ToDictionaryAsync(message => message.MessageId, cancellationToken);
        var mentions = await dbContext.ChatMessageMentions.AsNoTracking()
            .Where(mention => ids.Contains(mention.MessageId))
            .Select(mention => new ChatMessageMentionResponse(
                mention.MessageId, mention.MentionedUserId, mention.DisplayName))
            .ToListAsync(cancellationToken);
        var authorIds = messages.Select(message => message.UserId)
            .Concat(relatedMessages.Values.Select(message => message.UserId))
            .Distinct()
            .ToArray();
        var authorTeams = await userManager.Users.AsNoTracking()
            .Where(user => authorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.TeamId, cancellationToken);
        var mentionsByMessage = mentions.GroupBy(mention => mention.MessageId)
            .ToDictionary(group => group.Key, group => group.Select(mention =>
                new ChatMentionResponse(mention.UserId, mention.DisplayName)).ToArray());

        return messages.Select(message =>
        {
            relatedMessages.TryGetValue(message.ReplyToMessageId ?? Guid.Empty, out var reply);
            return new ChatMessageResponse(
                message.MessageId,
                message.GroupId,
                message.UserId,
                message.DisplayName,
                message.IsDeleted ? string.Empty : message.Content,
                DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc),
                message.IsDeleted,
                message.ReplyToMessageId,
                reply?.DisplayName,
                reply is null ? null : reply.IsDeleted ? "[deleted message]" : reply.Content,
                authorTeams.GetValueOrDefault(message.UserId),
                mentionsByMessage.GetValueOrDefault(message.MessageId, []));
        }).ToArray();
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Task<bool> IsMemberAsync(
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken) =>
        (from member in dbContext.ChatGroupMembers
         join chatGroup in dbContext.ChatGroups on member.GroupId equals chatGroup.GroupId
         where member.GroupId == groupId
               && member.UserId == userId
               && !chatGroup.IsArchived
         select member.UserId)
        .AnyAsync(cancellationToken);
}

public sealed record ChatGroupResponse(Guid GroupId, string Name);

public sealed record ChatGroupMemberResponse(Guid UserId, string DisplayName);

public sealed record ChatMentionResponse(Guid UserId, string DisplayName);

public sealed record ChatMessageMentionResponse(Guid MessageId, Guid UserId, string DisplayName);

public sealed record ChatMessageResponse(
    Guid MessageId,
    Guid GroupId,
    Guid UserId,
    string DisplayName,
    string Content,
    DateTime CreatedAtUtc,
    bool IsDeleted,
    Guid? ReplyToMessageId,
    string? ReplyToDisplayName,
    string? ReplyToContent,
    Guid? AuthorTeamId,
    IReadOnlyList<ChatMentionResponse> Mentions);

public sealed record SendChatMessageRequest(
    [Required, StringLength(2000, MinimumLength = 1)] string Content,
    Guid? ReplyToMessageId = null,
    IReadOnlyList<Guid>? MentionedUserIds = null);
