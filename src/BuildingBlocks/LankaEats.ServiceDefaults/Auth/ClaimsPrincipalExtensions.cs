using System.Security.Claims;

namespace LankaEats.ServiceDefaults.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(LankaEatsClaims.Subject)
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(LankaEatsRoles.Admin);
}
