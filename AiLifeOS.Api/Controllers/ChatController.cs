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
            .Select(message => new ChatMessageResponse(
                message.MessageId,
                message.GroupId,
                message.UserId,
                message.DisplayName,
                message.Content,
                message.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(messages);
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
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.ChatMessage_Create @MessageId={messageId}, @GroupId={groupId}, @UserId={userId}, @DisplayName={user.DisplayName}, @Content={content}",
            cancellationToken);

        var message = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(item => item.MessageId == messageId)
            .Select(item => new ChatMessageResponse(
                item.MessageId,
                item.GroupId,
                item.UserId,
                item.DisplayName,
                item.Content,
                item.CreatedAtUtc))
            .SingleAsync(cancellationToken);

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

public sealed record ChatMessageResponse(
    Guid MessageId,
    Guid GroupId,
    Guid UserId,
    string DisplayName,
    string Content,
    DateTime CreatedAtUtc);

public sealed record SendChatMessageRequest(
    [Required, StringLength(2000, MinimumLength = 1)] string Content);
