using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AiLifeOS.Api.Models;

public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("is_admin", user.IsAdmin ? "true" : "false"));
        identity.AddClaim(new Claim("is_active", user.IsActive ? "true" : "false"));
        identity.AddClaim(new Claim("must_change_password", user.MustChangePassword ? "true" : "false"));
        return identity;
    }
}
