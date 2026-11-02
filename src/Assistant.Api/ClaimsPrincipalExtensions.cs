using System.Security.Claims;
using Assistant.Domain.Identity;

namespace Assistant.Api;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Builds the caller identity from validated token claims only.</summary>
    public static UserContext ToUserContext(this ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Token has no user identifier.");

        var tenantId = principal.FindFirstValue("tid")
            ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid")
            ?? "unknown";

        var groups = principal.FindAll("groups").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        return new UserContext(userId, tenantId, groups);
    }
}
