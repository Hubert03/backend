using Microsoft.AspNetCore.Identity;

namespace AiLifeOS.Api.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
