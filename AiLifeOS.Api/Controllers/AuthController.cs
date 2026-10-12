using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AiLifeOS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AiLifeOsDbContext dbContext,
    IAntiforgery antiforgery,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserProfile_Upsert @UserId={user.Id}, @Email={email}, @DisplayName={user.DisplayName}, @CreatedAtUtc={user.CreatedAtUtc}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(ToResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(),
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Email or password is incorrect, or the account is temporarily locked." });
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Unauthorized(new { message = "Email or password is incorrect, or the account is temporarily locked." });
        }

        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
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

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();
        if (displayName.Length < 2 || displayName.Any(char.IsControl))
        {
            return BadRequest(new { message = "Display name must contain at least two visible characters." });
        }

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.EmailConfirmed = false;
        }

        user.DisplayName = displayName;
        user.Email = email;
        user.UserName = email;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserProfile_Upsert @UserId={user.Id}, @Email={email}, @DisplayName={user.DisplayName}, @CreatedAtUtc={user.CreatedAtUtc}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await signInManager.RefreshSignInAsync(user);

        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
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

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!await HasValidCsrfTokenAsync())
        {
            return BadRequest(new { message = "Invalid or missing anti-forgery token." });
        }

        await signInManager.SignOutAsync();
        return NoContent();
    }

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

    private static AuthResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName);
}

public sealed record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password);

public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(128)] string Password);

public sealed record UpdateProfileRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress, StringLength(320)] string Email);

public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 12)] string NewPassword);

public sealed record AuthResponse(Guid UserId, string Email, string DisplayName);
