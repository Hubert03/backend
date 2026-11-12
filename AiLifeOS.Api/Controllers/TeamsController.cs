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
[Authorize(Policy = "AdminOnly")]
[Route("api/teams")]
public sealed class TeamsController(
    AiLifeOsDbContext dbContext,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTeams(CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams.AsNoTracking()
            .OrderBy(team => team.Name)
            .Select(team => new
            {
                team.TeamId,
                team.Name,
                team.ManagerUserId,
            })
            .ToListAsync(cancellationToken);

        var teamIds = teams.Select(team => team.TeamId).ToArray();
        var managerIds = teams.Where(team => team.ManagerUserId.HasValue)
            .Select(team => team.ManagerUserId!.Value)
            .ToArray();
        var users = await userManager.Users.AsNoTracking()
            .Where(user => managerIds.Contains(user.Id) ||
                user.TeamId != null && teamIds.Contains(user.TeamId.Value) && user.IsActive)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                Email = user.Email ?? string.Empty,
                user.AccessRole,
                user.IsActive,
                user.TeamId,
            })
            .ToListAsync(cancellationToken);
        var names = users.ToDictionary(user => user.Id, user => user.DisplayName);
        var members = users.Where(user => user.TeamId.HasValue)
            .GroupBy(user => user.TeamId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TeamMemberResponse>)group
                .OrderBy(user => user.DisplayName)
                .Select(user => new TeamMemberResponse(
                    user.Id, user.DisplayName, user.Email, user.AccessRole, user.IsActive, user.TeamId))
                .ToArray());
        return Ok(teams.Select(team => new TeamResponse(
            team.TeamId,
            team.Name,
            team.ManagerUserId,
            team.ManagerUserId is Guid managerId ? names.GetValueOrDefault(managerId) : null,
            members.GetValueOrDefault(team.TeamId, []))));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTeam(
        CreateTeamRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.Teams.AnyAsync(team => team.Name == name, cancellationToken))
            return Conflict(new { message = "A team with this name already exists." });

        ApplicationUser? manager = null;
        if (request.ManagerUserId is Guid managerId)
        {
            manager = await userManager.Users.FirstOrDefaultAsync(
                user => user.Id == managerId && user.IsActive,
                cancellationToken);
            if (manager is null)
                return BadRequest(new { message = "Choose an active team manager." });
            if (manager.IsAdmin || manager.TeamId is not null)
                return BadRequest(new { message = "The selected manager is already assigned to an organization role or team." });
        }

        var team = new Team
        {
            Name = name,
            ManagerUserId = manager?.Id,
            CreatedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };
        dbContext.Teams.Add(team);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (manager is not null)
        {
            manager.TeamId = team.TeamId;
            manager.AccessRole = "Manager";
            var update = await userManager.UpdateAsync(manager);
            if (!update.Succeeded)
            {
                dbContext.Teams.Remove(team);
                await dbContext.SaveChangesAsync(cancellationToken);
                return BadRequest(new { errors = update.Errors.Select(error => error.Description) });
            }
        }

        return Created("/api/teams", ToResponse(team, manager is null ? [] : [ToMember(manager)]));
    }

    [HttpPost("{teamId:guid}/members")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(
        Guid teamId,
        AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        var team = await dbContext.Teams.FirstOrDefaultAsync(
            item => item.TeamId == teamId,
            cancellationToken);
        if (team is null) return NotFound();

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive)
            return BadRequest(new { message = "Choose an active user." });
        if (user.IsAdmin || user.AccessRole == "Manager" || user.TeamId is not null)
            return Conflict(new { message = "The selected user is already assigned to an organization role or team." });
        if (request.AccessRole is not ("TeamLead" or "User"))
            return BadRequest(new { message = "Team members can be assigned the TeamLead or User role." });

        user.TeamId = teamId;
        user.AccessRole = request.AccessRole;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });

        return NoContent();
    }

    [HttpDelete("{teamId:guid}/members/{userId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(
            item => item.Id == userId && item.TeamId == teamId,
            cancellationToken);
        if (user is null) return NotFound();
        if (user.AccessRole == "Manager")
            return BadRequest(new { message = "Assign a new team manager before removing the current manager." });

        user.TeamId = null;
        user.AccessRole = "User";
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        return NoContent();
    }

    private static TeamResponse ToResponse(Team team, IReadOnlyList<TeamMemberResponse> members) =>
        new(team.TeamId, team.Name, team.ManagerUserId, null, members);

    private static TeamMemberResponse ToMember(ApplicationUser user) =>
        new(user.Id, user.DisplayName, user.Email ?? string.Empty, user.AccessRole, user.IsActive, user.TeamId);
}

public sealed record CreateTeamRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    Guid? ManagerUserId);

public sealed record AddTeamMemberRequest(Guid UserId, string AccessRole);

public sealed record TeamResponse(
    Guid TeamId,
    string Name,
    Guid? ManagerUserId,
    string? ManagerName,
    IReadOnlyList<TeamMemberResponse> Members);

public sealed record TeamMemberResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    string AccessRole,
    bool IsActive,
    Guid? TeamId);
